namespace HpOptimizerCore
open Microsoft.ML.Sweeper.Algorithms
open System
open System.IO

module ParameterServer =
    open Microsoft.ML
    open Microsoft.ML.Sweeper
    open System.Threading

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
        //args.NumberInitialPopulation <- maxHistory + 1
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
    
    let resultSaver = MailboxProcessor.Start(fun inbox ->
        async {
            while true do
            let! (rc:HpRunResult),outPath = inbox.Receive()
            try
                use fs =File.AppendText(outPath)
                for r in rc.Result.ParameterSet do
                    fs.Write(r.Name,"=",r.ValueText,",")
                fs.WriteLine(rc.Result.MetricValue)
            with ex -> 
                printfn "Error saving result to %s %A" outPath ex.Message
        }
    )

    //code that when a client is connected
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
                                                              )
                                                    | Some _-> ()
                                                    let! reply = propose sweeper
                                                    printfn "%A" reply
                                                    rc.Reply(true, reply)

                    | Server_ProposeSweeps ps,rc -> ps |> Option.iter (fun ps -> 
                                                            resultSaver.Post(ps,outputPath)
                                                            sweeper.Update(ps.Id, ps.Result))
                                                    if count < maxIter then
                                                        let! reply = propose sweeper
                                                        rc.Reply(true, reply)
                                                        printfn "%A" reply

                                                    else
                                                        rc.Reply(false,Client_ServerDone)

                    | x -> failwithf "Unexpected message for server %A" x
                printfn "server done"
            with ex ->
                printfn "%A" ex.Message
        })
