open System.Diagnostics
open System
(*
It is recommened to use a copy of this script to maintiain
consistency between server and client processes
*)

let namedPipe = "model1"
let outputPath = @"C:\s\repodata\hpto\model_1.txt"  //save server output here - hyperparameter values and metric results
let maxIter  =  1000                                //number of iterations for the search
let batchSize = 10                                  //number of concurrent clients


let spawn cmdLine =
    let pi = ProcessStartInfo()
    pi.FileName <- "dotnet.exe"
    pi.Arguments <- " " + cmdLine
    pi.WindowStyle <- ProcessWindowStyle.Normal
    pi.UseShellExecute <- true
    let p = Process.Start(pi)
    printfn "started pid %d" p.Id

let serverDll = __SOURCE_DIRECTORY__ + @"\..\HpOptServer\bin\Debug\netcoreapp2.1\HpOptServer.dll"
let clientDll = __SOURCE_DIRECTORY__ + @"\..\HpOptModelRunner\bin\Debug\netcoreapp2.1\HpOptModelRunner.dll"

let serverCmd = sprintf "%s %s %s %d %d" serverDll namedPipe outputPath maxIter batchSize
let clientCmd = sprintf "%s %s" clientDll namedPipe

spawn serverCmd

for i in 1 .. batchSize do
    spawn clientCmd