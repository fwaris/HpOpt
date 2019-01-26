// Learn more about F# at http://fsharp.org

open System
open HpOptimizerCore
open HpOptimizerCore.Server

let isInt s = match Int32.TryParse(s:string) with true,_ -> true | _ -> false

[<EntryPoint>]
let main args =
    let q,p,m = 
        match args with 
        | [|q;p;maxIter|] when isInt maxIter -> q,p,(int maxIter)
        | _ -> 
            printfn "usage: namedPipeName outpath maxIterations"
            printfn "using defaults %s %s %d" Defaults.pipeName Defaults.outputPath Defaults.maxIter
            Defaults.pipeName, Defaults.outputPath, Defaults.maxIter
        
    //start server with handler factory to handle client connections
    let agent = Server.startServer q  (ParamterServer.handlerFactory p m) 

    while Console.ReadKey().KeyChar <> 'q' do
       Console.WriteLine("enter q to quit")

    agent.Post ServerControl.Stop //shut down server

    0 // return an integer exit code
