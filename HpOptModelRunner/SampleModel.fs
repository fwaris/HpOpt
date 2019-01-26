namespace SampleModel

open System
open Microsoft.ML
open Microsoft.ML.StaticPipe

module Train =

    (* 
    Data:
        download test / train datasets from here:
        https://github.com/dotnet/machinelearning/blob/master/test/data/wikipedia-detox-250-line-data.tsv
    *)

    //*** change this for your run
    let dataPath = @"C:\s\repodata\hpto\train.txt" 

    //helper functions for extracting label and score values
    let label struct (lbl,text,features,score,prob,predLbl) = lbl
    let pred struct (lbl,text,features,score,prob,predLbl) = struct(score,prob,predLbl)

    //train the model using the supplied hyperparameters
    let trainModel trees leaves learningRate = 

        let ctx = MLContext(Nullable 10)

        let reader = 
            TextLoaderStatic.CreateReader(
                                ctx, 
                                (fun (c:TextLoaderStatic.Context) -> 
                                    struct (
                                        c.LoadBool(0),
                                        c.LoadText(1)
                                    )),
                                separator = '\t',
                                hasHeader = true)

                          
        let trainData = reader.Read(dataPath)

        let pipeline = 
            reader
                .MakeNewEstimator()
                .Append(fun struct (lbl,text) -> 
                    let features = text.FeaturizeText()
                    let struct(score,probability,predictedLabel) = 
                            ctx.BinaryClassification.Trainers.FastTree(
                                    lbl,
                                    features,
                                    numTrees= trees,
                                    numLeaves = leaves,
                                    learningRate = learningRate,
                                    minDatapointsInLeaves=20
                                    )
                    struct(
                        lbl,
                        text,
                        features,
                        score,
                        probability,
                        predictedLabel
                        ))

        let metrics = ctx.BinaryClassification.CrossValidate(trainData, pipeline, label, numFolds=5)

        //return the average metric across all the folds
        //this metric is what will be optimized by the system
        //Here we are using AUC but you may also use accuracy or F1 score, etc.
        //not 100% sure about this but the metric should range between 0 - 1
        let m = metrics |> Seq.map(fun struct(m,a,b)->m.Auc) |> Seq.average                    
        printfn "trees=%d, leaves=%d, lr=%f -> %f" trees leaves learningRate m
        m

