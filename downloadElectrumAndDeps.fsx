#!/usr/bin/env fsharpi

open System
open System.IO
open System.Net
open System.Threading
open System.Security.Cryptography
open System.Linq
open System.Collections.Generic

#load "InfraLib/ProcessTools.fs"
#r "System.Configuration"
#load "InfraLib/MiscTools.fs"
#load "InfraLib/UnixTools.fs"
#load "InfraLib/NetTools.fs"
open Fsx.Infrastructure

let electrumTarGzUrl = "https://download.electrum.org/2.7.12/Electrum-2.7.12.tar.gz"
let electrumSha256Sum = "3644274231326cc6b13a25703ebe6ec22bbbce7832a86d75abc745040475ad6f"
let electrumDeps = [| "python-qt4"; "python-setuptools"; "python-pip" |]

let currentDir = Directory.GetCurrentDirectory()
let binDir = Path.Combine(currentDir, "bin")
if not (Directory.Exists(binDir)) then
    Directory.CreateDirectory(binDir) |> ignore
Directory.SetCurrentDirectory(binDir)
try
    let electrumTgz = NetTools.SafeDownloadFile(new Uri(electrumTarGzUrl), electrumSha256Sum)
    UnixTools.DownloadAptPackagesRecursively(electrumDeps)
    ProcessTools.SafeExec("pip", "download " + electrumTgz.Name, true)

    Console.WriteLine("Success, now do this in the offline computer:")
    Console.WriteLine("sudo dpkg --install *.deb")
    Console.WriteLine("sudo pip install {0} --no-index --find-links `pwd`", electrumTgz.Name)
finally
    Directory.SetCurrentDirectory(currentDir)
