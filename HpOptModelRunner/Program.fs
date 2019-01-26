namespace SampleModel
open System
open HpOptimizerCore

module Pgm =
    open Microsoft.ML
    open Microsoft.ML.Sweeper

    //hyperparameter names
    let p_trees = "trees"
    let p_leaves = "leaves"
    let p_learningRate = "learningRate"

    //define the hyperparamters that need to be optimized
    let hyperParameters : IValueGenerator[] = 
        [|
            FloatParamArguments (Name=p_trees, Min=5.0f, Max=100.0f, StepSize=Nullable 5.0 )
            FloatParamArguments (Name=p_leaves, Min=5.0f, Max=100.0f, StepSize=Nullable 5.0)
            FloatParamArguments (Name=p_learningRate, Min=0.01f, Max=0.1f, StepSize=Nullable 0.01)
            
        |]
        |> Array.map (fun x -> FloatValueGenerator(x) :> _) 


    //define a wrapper method to train model 
    //that deals with conversion from/to sweeper 
    //interface objects
    let trainWithParms (parmSet:ParameterSet) =
        let trees           = parmSet.[p_trees].ValueText        |> float |> int
        let leaves          = parmSet.[p_leaves].ValueText       |> float |> int
        let learningRate    = parmSet.[p_learningRate].ValueText |> float
        let auc = Train.trainModel trees leaves learningRate
        RunResult(parmSet, auc, true)

    let run sweep = sweep |> Option.map(fun {Id=i; Parms=p} -> {Id=i; Result=p |> ParameterSet |>  trainWithParms})

    //loop to continually run the parameter search
    //search ends when server terminates the pipe
    let runModel namedPipe  =
        let pipe = Client.openPipe namedPipe
        pipe.Connect()
        let agent = Client.agent pipe
        let mutable lastRslt = None
        async {
            try 
                    let! initSweep = Client.initServer agent 1000 hyperParameters 
                    lastRslt <- run initSweep
                    while true do
                        if lastRslt.IsNone then
                            do! Async.Sleep 5000 // wait 
                        let! sweep = Client.propose agent 1  lastRslt
                        lastRslt <- run sweep
            with ex -> 
                pipe.Close()
            }

    [<EntryPoint>]
    let main argv =
        let namedPipe = 
            match argv with 
            | [|q|] -> q
            | _ -> 
                printfn "usage: namedPipeName"
                printfn "using default namedPipeName %s" Defaults.pipeName
                Defaults.pipeName

        runModel namedPipe |> Async.Start
        while Console.ReadKey().KeyChar <> 'q' do
            Console.WriteLine("press q to quit")
        0 // return an integer exit code
