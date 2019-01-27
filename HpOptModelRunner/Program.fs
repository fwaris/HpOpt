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

    //***********
    //A: Define hyperparameters that parameterize your model
    //**********
    let hyperParameters : IValueGenerator[] = 
        [|
            FloatParamArguments (Name=p_trees, Min=5.0f, Max=100.0f, StepSize=Nullable 5.0 )
            FloatParamArguments (Name=p_leaves, Min=5.0f, Max=100.0f, StepSize=Nullable 5.0)
            FloatParamArguments (Name=p_learningRate, Min=0.01f, Max=0.1f, StepSize=Nullable 0.01)
            
        |]
        |> Array.map (fun x -> FloatValueGenerator(x) :> _) 


    //***********
    //B: Define a function that accepts a ParameteSet; trains your model; and returns a RunResult
    //**********
    let trainWithParms (parmSet:ParameterSet) =

        //parmSet contains the values of the hyperparameters proposed by the server
        let trees           = parmSet.[p_trees].ValueText        |> float |> int
        let leaves          = parmSet.[p_leaves].ValueText       |> float |> int
        let learningRate    = parmSet.[p_learningRate].ValueText |> float

        let auc = Train.trainModel trees leaves learningRate

        RunResult(parmSet, auc, true)


    [<EntryPoint>]
    let main argv =
        let namedPipe = 
            match argv with 
            | [|q|] -> q
            | _ -> 
                printfn "usage: namedPipeName"
                printfn "using default namedPipeName %s" Defaults.pipeName
                Defaults.pipeName

        //***********
        //C: In the 'main' function start Client.runModel with the correct namedPipe name, hyperparameters & trainer function
        //**********
        Client.runModel namedPipe hyperParameters trainWithParms |> Async.Start

        while Console.ReadKey().KeyChar <> 'q' do
            Console.WriteLine("press q to quit")
        0 // return an integer exit code
