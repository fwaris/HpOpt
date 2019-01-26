open System.Diagnostics
open System

let namedPipe = "model1"
let outputPath = @"C:\s\repodata\hpto\model_1.xml"
let maxIter  =  1000

let spawn cmdLine =
    let pi = ProcessStartInfo()
    pi.FileName <- "dotnet.exe"
    pi.Arguments <- " " + cmdLine
    pi.WindowStyle <- ProcessWindowStyle.Normal
    pi.UseShellExecute <- true
    let p = Process.Start(pi)
    printfn "started pid %d" p.Id

let serverDll = @"C:\s\Repos\hpto\HpOptServer\bin\Debug\netcoreapp2.1\HpOptServer.dll"
let clientDll = @"C:\s\Repos\hpto\HpOptModelRunner\bin\Debug\netcoreapp2.1\HpOptModelRunner.dll"

let serverCmd = sprintf "%s %s %s %d" serverDll namedPipe outputPath maxIter
let clientCmd = sprintf "%s %s" clientDll namedPipe

spawn serverCmd
for i in 1 .. 10 do
    spawn clientCmd