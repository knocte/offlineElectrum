#!/usr/bin/env fsharpi

open System
open System.IO
open System.Net
open System.Threading
open System.Security.Cryptography
open System.Linq
open System.Collections.Generic

#r "System.Configuration"
#load "InfraLib/MiscTools.fs"
#load "InfraLib/ProcessTools.fs"
#load "InfraLib/UnixTools.fs"
#load "InfraLib/NetTools.fs"
open Fsx.Infrastructure

let electrumDeps = [ "python-qt4"; "python-setuptools"; "python-pip" ]

type Coin = Bitcoin | Litecoin

let rec askCoin() =
    Console.Write("Do you want to setup Electrum for BTC(0) or LTC(1)? ")
    let input = Console.ReadLine()
    match System.Int32.TryParse(input) with
    | (true,coin) ->
        match coin with
        | 0 -> Coin.Bitcoin
        | 1 -> Coin.Litecoin
        | _ -> askCoin()
    | _ -> askCoin()

let coin = askCoin()

let binary,hash =
    if coin = Coin.Bitcoin then
        "https://download.electrum.org/2.7.12/Electrum-2.7.12.tar.gz","3644274231326cc6b13a25703ebe6ec22bbbce7832a86d75abc745040475ad6f"
    else
        "https://electrum-ltc.org/download/Electrum-LTC-2.6.4.2.tar.gz","64d9d6c3c4ceda8a73e1abe454d41b137262d7fad5406e6d8cea4d63cdf40c6b"

let deps =
    if coin = Coin.Bitcoin then
        electrumDeps
    else
        "python-dev"::("python-slowaes"::electrumDeps)

let currentDir = Directory.GetCurrentDirectory()
let binDir = Path.Combine(currentDir, "bin")
if not (Directory.Exists(binDir)) then
    Directory.CreateDirectory(binDir) |> ignore
Directory.SetCurrentDirectory(binDir)
try
    let electrumTgz = NetTools.SafeDownloadFile(new Uri(binary), hash)
    UnixTools.DownloadAptPackagesRecursively(deps)
    ProcessTools.SafeExec("pip", "download " + electrumTgz.Name, true)

    Console.WriteLine("Success, now copy all files in bin/ to your offline computer, and execute these commands there:")
    Console.WriteLine("sudo dpkg --install *.deb")
    Console.WriteLine("sudo pip install {0} --no-index --find-links `pwd`", electrumTgz.Name)
finally
    Directory.SetCurrentDirectory(currentDir)
