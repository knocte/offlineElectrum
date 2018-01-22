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

let electrumDeps = [ "python3-pyqt5"; "python3-setuptools"; "python3-pip" ]

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
        "https://download.electrum.org/3.0.5/Electrum-3.0.5.tar.gz","61ebc06782433f928853188762c6f3098bd3d08d54c34b5594233d196e51e01b"
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
    ProcessTools.SafeExec("pip3", "download " + electrumTgz.Name, true)

    Console.WriteLine("Success, now copy all files in bin/ to your offline computer, and execute these commands there:")
    Console.WriteLine("sudo dpkg --install *.deb")
    Console.WriteLine("sudo pip3 install {0} --no-index --find-links `pwd`", electrumTgz.Name)
finally
    Directory.SetCurrentDirectory(currentDir)
