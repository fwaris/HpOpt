namespace HpOptimizerCore

//parameter server - hosts the IAsyncSweeper instance
//manages updates from clients
//requests hyperparameter proposals from IAsyncSweeper
//on behalf of the clients

open Microsoft.ML.Sweeper.Algorithms
open System
open System.IO

module ParameterServer =
    open Microsoft.ML
    open Microsoft.ML.Sweeper
    open System.Threading
    open System.Text

    let createGenerator gen =
        { new IComponentFactory<IValueGenerator> with
            member x.CreateComponent(ctx) = gen
        }

    let createSweeper gen =
        { new IComponentFactory<ISweeper> with
            member x.CreateComponent(ctx) = gen
        }

    let initSweeperKdo ctx  (parms:IValueGenerator[]) maxHistory batchSize = 
        let args = KdoSweeper.Arguments()
        args.SweptParameters <- parms |> Array.map(createGenerator)
        args.HistoryLength <- maxHistory + 1                       
        args.NumberInitialPopulation <- maxHistory + 1 
        let baseSweeper = KdoSweeper(ctx,args)
        let dsa = DeterministicSweeperAsync.Arguments()
        dsa.Sweeper <- createSweeper baseSweeper
        dsa.BatchSize <- batchSize
        dsa.Relaxation <- 1
        new DeterministicSweeperAsync(ctx, dsa)

    let initSweeperNM ctx  (parms:IValueGenerator[]) maxHistory batchSize = 
        let args = NelderMeadSweeper.Arguments()
        args.SweptParameters <- parms |> Array.map(createGenerator)
        let baseSweeper = NelderMeadSweeper(ctx,args)
        let dsa = DeterministicSweeperAsync.Arguments()
        dsa.Sweeper <- createSweeper baseSweeper
        dsa.BatchSize <- batchSize
        dsa.Relaxation <- 0
        new DeterministicSweeperAsync(ctx, dsa)

    let private ctx = MLContext(Nullable 10)
    let mutable private sweeper:IAsyncSweeper = Unchecked.defaultof<_>
    let mutable private sweepInit = None
    let mutable private count = 0
    let private lck = obj()

    let propose (sweeper:IAsyncSweeper) =
        async {
            let! parmSet = sweeper.Propose() |> Async.AwaitTask
            let ps = 
                if parmSet = null then 
                    None 
                else
                    Interlocked.Add(&count,1) |> ignore
                    {Id=parmSet.Id; Parms=parmSet.ParameterSet |> Seq.toArray} |> Some

            return Client_RunParameters ps
        }

    let printResult (rr:HpRunResult) =
        let sb = new StringBuilder()
        for r in rr.Result.ParameterSet do
            sb.Append(r.Name).Append("=").Append(r.ValueText).Append(",") |> ignore
        sb.Append(rr.Result.MetricValue) |> ignore
        sb.ToString()

    let checkNewBest bestM (rr:HpRunResult) rs =
        match rr.Result.IsMetricMaximizing, rr.Result.MetricValue, !bestM with
        | _,n,None                   -> bestM := Some n
        | true,n, Some o when n > o  -> bestM := Some n; printfn "new max %s" rs
        | false,n,Some o when n < o  -> bestM := Some n; printfn "new min %s" rs
        | _                          -> ()
    
    let resultHandler = MailboxProcessor.Start(fun inbox ->
        let bestM = ref None
        async {
            while true do
            let! rr,outPath = inbox.Receive()
            try
                let rs = printResult rr
                checkNewBest bestM rr rs
                use fs =File.AppendText(outPath)
                fs.WriteLine(rs)
            with ex -> 
                printfn "Error saving result to %s %A" outPath ex.Message
        }
    )

    //code that runs when a client is connected
    let handlerFactory outputPath maxIter batchSize () = MailboxProcessor.Start (fun (inbox:MailboxProcessor<Message>) ->
        async {
            try
                while count < maxIter do
                    match! inbox.Receive() with 

                    | Server_Init init,rc        -> match sweepInit with
                                                    | None -> 
                                                              lock lck (fun () ->
                                                                  if sweepInit.IsNone then
                                                                      sweepInit <- Some init
                                                                      sweeper <- initSweeperKdo ctx init.Parms maxIter batchSize
                                                                      //sweeper <- initSweeperNM ctx init.Parms maxIter batchSize
                                                              )
                                                    | Some _-> ()
                                                    let! reply = propose sweeper
                                                    rc.Reply(true, reply)

                    | Server_ProposeSweeps ps,rc -> ps |> Option.iter (fun ps -> 
                                                            resultHandler.Post(ps,outputPath)
                                                            sweeper.Update(ps.Id, ps.Result))
                                                    if count < maxIter then
                                                        let! reply = propose sweeper
                                                        rc.Reply(true, reply)

                                                    else
                                                        rc.Reply(false,Client_ServerDone)

                    | x -> failwithf "Unexpected message for server %A" x
                printfn "server done"
            with ex ->
                printfn "%A" ex.Message
        })
