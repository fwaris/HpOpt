// Learn more about F# at http://fsharp.org

open System
open HpOptimizerCore
open HpOptimizerCore.Server

let isInt s = match Int32.TryParse(s:string) with true,_ -> true | _ -> false

[<EntryPoint>]
let main args =
    let q,p,m,b = 
        match args with 
        | [|q;p;maxIter;batchSize|] when isInt maxIter && isInt batchSize  -> q,p,(int maxIter),(int batchSize)
        | _ -> 
            printfn "usage: namedPipeName outpath maxIterations batchSize"
            printfn "using defaults %s %s %d %d" Defaults.pipeName Defaults.outputPath Defaults.maxIter Defaults.batchSize
            Defaults.pipeName, Defaults.outputPath, Defaults.maxIter, Defaults.batchSize
        
    //start server with handler factory to handle client connections
    //let sweeperAgent = ParameterServer.sweeperAgent p m b
    let agent = Server.startServer q  (ParameterServer.handlerFactory p m b) 

    while Console.ReadKey().KeyChar <> 'q' do
       Console.WriteLine("enter q to quit")

    agent.Post ServerControl.Stop //shut down server

    0 // return an integer exit code
