namespace HpOptimizerCore
open Microsoft.ML.Sweeper.Algorithms
open System
open System.IO

module ParamterServer =
    open Microsoft.ML
    open Microsoft.ML.Sweeper

    let createGenerator gen =
        { new IComponentFactory<IValueGenerator> with
            member x.CreateComponent(ctx) = gen
        }

    let createSweeper gen =
        { new IComponentFactory<ISweeper> with
            member x.CreateComponent(ctx) = gen
        }

    let initSweeper ctx  (parms:IValueGenerator[]) maxHistory = 
        let args = KdoSweeper.Arguments()
        args.SweptParameters <- parms |> Array.map(createGenerator)
        args.HistoryLength <- maxHistory
        args.NumberInitialPopulation <- maxHistory
        let baseSweeper = KdoSweeper(ctx,args)
        let dsa = DeterministicSweeperAsync.Arguments()
        dsa.Sweeper <- createSweeper baseSweeper
        new DeterministicSweeperAsync(ctx, dsa)

    let initSweeperNM ctx  (parms:IValueGenerator[]) maxHistory = 
        let args = NelderMeadSweeper.Arguments()
        args.SweptParameters <- parms |> Array.map(createGenerator)
        let baseSweeper = NelderMeadSweeper(ctx,args)
        let dsa = DeterministicSweeperAsync.Arguments()
        dsa.Sweeper <- createSweeper baseSweeper
        new DeterministicSweeperAsync(ctx, dsa)

    let propose (sweeper:IAsyncSweeper) count =
        async {
            let! parmSet = sweeper.Propose() |> Async.AwaitTask
            let ps = 
                if parmSet = null then 
                    None 
                else
                    count := !count + 1
                    {Id=parmSet.Id; Parms=parmSet.ParameterSet |> Seq.toArray} |> Some

            return Client_RunParameters ps
        }
    
    let resultSaver = MailboxProcessor.Start(fun inbox ->
        async {
            while true do
            let! rc,outPath = inbox.Receive()
            try
                use fs = new StreamWriter(File.OpenWrite(outPath))
                Server.xmlSerializer.Serialize(fs,rc)
            with ex -> 
                printfn "Error saving result to %s %A" outPath ex.Message
        }
    )


    let handlerFactory outputPath maxIter () = MailboxProcessor.Start (fun (inbox:MailboxProcessor<Message>) ->
        let ctx = MLContext(Nullable 10)
        let mutable sweeper:IAsyncSweeper = Unchecked.defaultof<_>
        let mutable sweepInit = None
        let count = ref 0
        async {
            try
                while !count < maxIter do
                    match! inbox.Receive() with 

                    | Server_Init init,rc        -> match sweepInit with
                                                    | None -> sweepInit <- Some init
                                                              sweeper <- initSweeper ctx init.Parms init.MaxHistory
                                                    | Some _-> ()
                                                    let! reply = propose sweeper count
                                                    rc.Reply(true, reply)

                    | Server_ProposeSweeps ps,rc -> ps |> Option.iter (fun ps -> 
                                                            resultSaver.Post(ps,outputPath)
                                                            sweeper.Update(ps.Id, ps.Result))
                                                    let! reply = propose sweeper count
                                                    rc.Reply(true, reply)

                    | x -> failwithf "Unexpected message for server %A" x
                printfn "server done"
            with ex ->
                printfn "%A" ex.Message
        })