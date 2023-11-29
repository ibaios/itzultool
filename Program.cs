using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace ItzulTool
{
    class Program
    {

        public static void PrintHelp()
        {
            Console.WriteLine("ItzulTool");
            Console.WriteLine("=========");
            Console.WriteLine("");
            Console.WriteLine("Aukerak:");
            Console.WriteLine("");
            Console.WriteLine("Deskonprimatzeko: ItzulTool decompress <fitxategia> (deskonprimatutako fitxategiak jatorrizkoaren izen bera izango du, bukaeran '.decomp' luzapena gehituta)");
            Console.WriteLine("Konprimatzeko: ItzulTool compress <jatorriko fitxategia> <helburuko fitxategia>");
            Console.WriteLine("EMIPa aplikatzeko: ItzulTool applyemip <emip fitxategia> <direktorioa>");
            Console.WriteLine("Bundle batetik assets fitxategi bat erauzteko: ItzulTool extractassets <bundlea> <asseta>");
            Console.WriteLine("Bundle bateko assets fitxategi bat ordezkatzeko: ItzulTool replaceassets <bundlea> <asset berria bide-izenarekin>");
            Console.WriteLine("");
        }

        private static AssetBundleFile DecompressBundle(string file, string decompFile)
        {
            AssetBundleFile bun = new AssetBundleFile();

            Stream fs = File.OpenRead(file);
            AssetsFileReader r = new AssetsFileReader(fs);

            bun.Read(r);
            if (bun.Header.GetCompressionType() != 0)
            {
                Stream nfs;
                if (decompFile == null)
                    nfs = new MemoryStream();
                else
                    nfs = File.Open(decompFile, FileMode.Create, FileAccess.ReadWrite);

                AssetsFileWriter w = new AssetsFileWriter(nfs);
                bun.Unpack(w);

                nfs.Position = 0;
                fs.Close();

                fs = nfs;
                r = new AssetsFileReader(fs);

                bun = new AssetBundleFile();
                bun.Read(r);
            } else {
                System.IO.File.Copy(file, decompFile);
            }

            return bun;
        }

        private static string GetNextBackup(string affectedFilePath)
        {
            for (int i = 0; i < 10000; i++)
            {
                string bakName = $"{affectedFilePath}.bak{i.ToString().PadLeft(4, '0')}";
                if (!File.Exists(bakName))
                {
                    return bakName;
                }
            }

            Console.WriteLine("Backup gehiegi daude, ezabatu soberan daudenak eta saiatu berriro.");
            return null;
        }

        private static HashSet<string> GetFlags(string[] args)
        {
            HashSet<string> flags = new HashSet<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("-"))
                    flags.Add(args[i]);
            }
            return flags;
        }

        private static void CompressBundle(string file, string destFile) {
            var am = new AssetsManager();
            var bundleInst = am.LoadBundleFile(file, false);
            var path = destFile;
            var compType = AssetBundleCompressionType.LZ4;

            var progress = new CommandLineProgressBar();

            using (FileStream fs = File.Open(path, FileMode.Create))
            using (AssetsFileWriter w = new AssetsFileWriter(fs))
            {
                bundleInst.file.Pack(bundleInst.file.Reader, w, compType, true, progress);
            }
        }

        private static void Decompress(string[] args)
        {
            Console.WriteLine("Deskonprimatzen...");

            var file = args[1];
            DecompressBundle(file, $"{file}.decomp");

            Console.WriteLine("Deskonprimatuta.");
        }

        private static void Compress(string[] args)
        {
            Console.WriteLine("Konprimatzen...");

            var file = args[1];
            var destFile = args[2];
            CompressBundle(file, destFile);

            Console.WriteLine("Konprimatuta.");
        }

        private static void ApplyEmip(string[] args)
        {
            HashSet<string> flags = GetFlags(args);
            string emipFile = args[1];
            string rootDir = args[2];

            if (!File.Exists(emipFile))
            {
                Console.WriteLine($"Ez da {emipFile} fitxategia existitzen!");
                return;
            }

            InstallerPackageFile instPkg = new InstallerPackageFile();
            FileStream fs = File.OpenRead(emipFile);
            AssetsFileReader r = new AssetsFileReader(fs);
            instPkg.Read(r, true);

            Console.WriteLine($"EMIPa instalatzen...");
            Console.WriteLine($"Paketea: {instPkg.modName} - Egilea: {instPkg.modCreators}");
            Console.WriteLine(instPkg.modDescription);

            foreach (var affectedFile in instPkg.affectedFiles)
            {
                string affectedFileName = Path.GetFileName(affectedFile.path);
                string affectedFilePath = Path.Combine(rootDir, affectedFile.path);

                if (affectedFile.isBundle)
                {
                    string decompFile = $"{affectedFilePath}.decomp";
                    string modFile = $"{affectedFilePath}.mod";
                    string bakFile = GetNextBackup(affectedFilePath);

                    if (bakFile == null)
                        return;

                    if (flags.Contains("-md"))
                        decompFile = null;

                    Console.WriteLine($"{affectedFileName} deskonprimatzen, helburua: {decompFile??"memoria"}...");
                    AssetBundleFile bun = DecompressBundle(affectedFilePath, decompFile);
                    List<BundleReplacer> reps = new List<BundleReplacer>();

                    foreach (var rep in affectedFile.replacers)
                    {
                        var bunRep = (BundleReplacer)rep;
                        if (bunRep is BundleReplacerFromAssets)
                        {
                            //read in assets files from the bundle for replacers that need them
                            string assetName = bunRep.GetOriginalEntryName();
                            var bunRepInf = BundleHelper.GetDirInfo(bun, assetName);
                            long pos = bunRepInf.Offset;
                            bunRep.Init(bun.DataReader, pos, bunRepInf.DecompressedSize);
                        }
                        reps.Add(bunRep);
                    }

                    Console.WriteLine($"{modFile} idazten...");
                    FileStream mfs = File.Open(modFile, FileMode.Create);
                    AssetsFileWriter mw = new AssetsFileWriter(mfs);
                    bun.Write(mw, reps, instPkg.addedTypes); //addedTypes does nothing atm
                    
                    mfs.Close();
                    bun.Close();

                    Console.WriteLine($"Mod fitxategia ordezkatzen...");
                    File.Move(affectedFilePath, bakFile);
                    File.Move(modFile, affectedFilePath);
                    
                    if (!flags.Contains("-kd") && !flags.Contains("-md") && File.Exists(decompFile))
                        File.Delete(decompFile);

                    Console.WriteLine($"Eginda.");
                }
                else //isAssetsFile
                {
                    string modFile = $"{affectedFilePath}.mod";
                    string bakFile = GetNextBackup(affectedFilePath);

                    if (bakFile == null)
                        return;

                    FileStream afs = File.OpenRead(affectedFilePath);
                    AssetsFileReader ar = new AssetsFileReader(afs);
                    AssetsFile assets = new AssetsFile();
                    assets.Read(ar);
                    List<AssetsReplacer> reps = new List<AssetsReplacer>();

                    foreach (var rep in affectedFile.replacers)
                    {
                        var assetsReplacer = (AssetsReplacer)rep;
                        reps.Add(assetsReplacer);
                    }

                    Console.WriteLine($"{modFile} idazten...");
                    FileStream mfs = File.Open(modFile, FileMode.Create);
                    AssetsFileWriter mw = new AssetsFileWriter(mfs);
                    assets.Write(mw, 0, reps, instPkg.addedTypes);

                    mfs.Close();
                    ar.Close();

                    Console.WriteLine($"Mod fitxategia ordezkatzen...");
                    File.Move(affectedFilePath, bakFile);
                    File.Move(modFile, affectedFilePath);

                    Console.WriteLine($"Eginda.");
                }
            }

            return;
        }

        
        private static void ExtractAssetsFileFromBundle(string[] args)
        {
            var bundle = args[1];
            var assetsFileName = args[2];

            Console.WriteLine($"{assetsFileName} assets fitxategia erauzten...");

            var am = new AssetsManager();
            var bundleInst = am.LoadBundleFile(bundle, false);
            AssetBundleFile bun = bundleInst.file;
            var exportDirectory = Path.GetDirectoryName(bundleInst.path);

            Console.WriteLine("Esportatzeko direktorioa:" + exportDirectory);

            int entryCount = bun.BlockAndDirInfo.DirectoryInfos.Length;
            for (int i = 0; i < entryCount; i++)
            {
                string name = bun.BlockAndDirInfo.DirectoryInfos[i].Name;
                if(name.Equals(assetsFileName))
                {
                    byte[] data = BundleHelper.LoadAssetDataFromBundle(bun, i);
                    string filePath = Path.Combine(exportDirectory, name);
                    Console.WriteLine($"{filePath} esportatzen...");
                    System.IO.FileInfo file = new System.IO.FileInfo(filePath);
                    File.WriteAllBytes(filePath, data);
                }
            }

            Console.WriteLine("Assets fitxategia erauzita.");
        }

        private static void ReplaceAssetsFileInBundle(string[] args)
        {

            var bundle = args[1];
            var assetsFile = args[2];
            var assetsFileName = Path.GetFileName(assetsFile);

            Console.WriteLine($"{assetsFileName} assets fitxategia ordezkatzen...");

            var am = new AssetsManager();
            var bundleInst = am.LoadBundleFile(bundle, false);
            AssetBundleFile bun = bundleInst.file;

            List<BundleReplacer> reps = new List<BundleReplacer>();
            List<Stream> streams = new List<Stream>();

            int entryCount = bun.BlockAndDirInfo.DirectoryInfos.Length;
            for (int i = 0; i < entryCount; i++)
            {
                string name = bun.BlockAndDirInfo.DirectoryInfos[i].Name;
                
                if (name.Equals(assetsFileName))
                {
                    FileStream fs = File.OpenRead(assetsFile);
                    long length = fs.Length;
                    reps.Add(new BundleReplacerFromStream(name, name, true, fs, 0, length));
                    streams.Add(fs);
                    Console.WriteLine($"{name} inportatzen...");
                }
            }

            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            using (AssetsFileWriter w = new AssetsFileWriter(ms))
            {
                bun.Write(w, reps);
                data = ms.ToArray();
            }
            Console.WriteLine($"{bundle} bundleari aldaketak aplikatzen...");

            foreach (Stream stream in streams)
                stream.Close();

            bun.Close();

            File.WriteAllBytes(bundle, data);

            Console.WriteLine("Eginda.");

        }

        
        static void Main(string[] args)
        {

            if (args.Length < 2)
            {
                PrintHelp();
                return;
            }
            
            string command = args[0];

            if (command == "decompress")
            {
                Decompress(args);
            }
            else if (command == "compress")
            {
                Compress(args);
            }
            else if (command == "applyemip")
            {
                ApplyEmip(args);
            }
            else if (command == "extractassets")
            {
                ExtractAssetsFileFromBundle(args);
            }
            else if (command == "replaceassets")
            {
                ReplaceAssetsFileInBundle(args);
            }


            

        }
    }
}
