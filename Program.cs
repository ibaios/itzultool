using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            Console.WriteLine("Deskonprimatzeko: itzultool-sdk decompress <fitxategia>");
            Console.WriteLine("Konprimatzeko: itzultool-sdk compress <fitxategia>");
            Console.WriteLine("EMIPa aplikatzeko: itzultool-sdk applyemip <emip fitxategia> <direktorioa>");
            Console.WriteLine("Bundle batetik assets fitxategi bat erauzteko: itzultool-sdk extractassets <bundlea> <asseta>");
            Console.WriteLine("Bundle bateko assets fitxategi bat ordezkatzeko: itzultool-sdk replaceassets <bundlea> <asset berria bide-izenarekin> (bundlea konprimatua bazegoen, komando honek deskonprimatu egingo du)");
#if SDK
            Console.WriteLine("Assets fitxategi batetik baliabide bat JSON formatuan erauzteko: itzultool-sdk extractasjson <assets fitxategia> <baliabidearen izena>");
#endif
            Console.WriteLine("");
        }

        private static AssetBundleFile DecompressBundle(string file)
        {
            AssetBundleFile bun = new AssetBundleFile();
            var tempPath = file + ".tmp";

            Stream fs = File.OpenRead(file);
            AssetsFileReader r = new AssetsFileReader(fs);

            bun.Read(r);
            if (bun.Header.GetCompressionType() != 0)
            {
                using (Stream nfs = File.Open(tempPath, FileMode.Create, FileAccess.ReadWrite))
                {
                    AssetsFileWriter w = new AssetsFileWriter(nfs);
                    bun.Unpack(w);
                }

                fs.Close();
                File.Move(tempPath, file, overwrite: true);

                fs = File.OpenRead(file);
                r = new AssetsFileReader(fs);

                bun = new AssetBundleFile();
                bun.Read(r);
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

        private static void CompressBundle(string file) {
            var am = new AssetsManager();
            var bundleInst = am.LoadBundleFile(file, false);
            var compType = AssetBundleCompressionType.LZ4;
            var tempPath = file + ".tmp";

            var progress = new CommandLineProgressBar();

            using (FileStream fs = File.Open(tempPath, FileMode.Create))
            using (AssetsFileWriter w = new AssetsFileWriter(fs))
            {
                bundleInst.file.Pack(w, compType, true, progress);
            }

            am.UnloadAll(true);
            File.Move(tempPath, file, overwrite: true);
        }

        private static void Decompress(string[] args)
        {
            Console.WriteLine("Deskonprimatzen...");

            var file = args[1];

            DecompressBundle(file);

            Console.WriteLine("Deskonprimatuta.");
        }

        private static void Compress(string[] args)
        {
            Console.WriteLine("Konprimatzen...");

            var file = args[1];
            CompressBundle(file);

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
                    string modFile = $"{affectedFilePath}.mod";
                    string bakFile = GetNextBackup(affectedFilePath);

                    if (bakFile == null)
                        return;

                    Console.WriteLine($"{affectedFileName} deskonprimatzen...");
                    AssetBundleFile bun = DecompressBundle(affectedFilePath);

                    foreach (var bunRep in affectedFile.bundleReplacers)
                    {
                        var dirInfo = BundleHelper.GetDirInfo(bun, bunRep.OldName);
                        AssetsFile assetsFile = BundleHelper.LoadAssetFromBundle(bun, bunRep.OldName);

                        foreach (var assetRep in bunRep.AssetReplacers)
                        {
                            var assetInfo = assetsFile.GetAssetInfo(assetRep.PathId);
                            if (assetRep.IsRemover)
                                assetInfo.SetRemoved();
                            else
                                assetInfo.SetNewData(assetRep.Data);
                        }

                        dirInfo.Replacer = new ContentReplacerFromAssets(assetsFile);
                    }

                    Console.WriteLine($"{modFile} idazten...");
                    FileStream mfs = File.Open(modFile, FileMode.Create);
                    AssetsFileWriter mw = new AssetsFileWriter(mfs);
                    bun.Write(mw);
                    
                    mfs.Close();
                    bun.Close();

                    Console.WriteLine($"Mod fitxategia ordezkatzen...");
                    File.Move(affectedFilePath, bakFile);
                    File.Move(modFile, affectedFilePath);
                    
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
                    foreach (var assetRep in affectedFile.assetReplacers)
                    {
                        var assetInfo = assets.GetAssetInfo(assetRep.PathId);
                        if (assetRep.IsRemover)
                            assetInfo.SetRemoved();
                        else
                            assetInfo.SetNewData(assetRep.Data);
                    }

                    Console.WriteLine($"{modFile} idazten...");
                    FileStream mfs = File.Open(modFile, FileMode.Create);
                    AssetsFileWriter mw = new AssetsFileWriter(mfs);
                    assets.Write(mw, 0);

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

            int entryCount = bun.BlockAndDirInfo.DirectoryInfos.Count;
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

            List<Stream> streams = new List<Stream>();

            int entryCount = bun.BlockAndDirInfo.DirectoryInfos.Count;
            for (int i = 0; i < entryCount; i++)
            {
                string name = bun.BlockAndDirInfo.DirectoryInfos[i].Name;

                if (name.Equals(assetsFileName))
                {
                    FileStream fs = File.OpenRead(assetsFile);
                    long length = fs.Length;
                    bun.BlockAndDirInfo.DirectoryInfos[i].Replacer = new ContentReplacerFromStream(fs, 0, (int)length);
                    streams.Add(fs);
                    Console.WriteLine($"{name} inportatzen...");
                }
            }

            byte[] data;
            using (MemoryStream ms = new MemoryStream())
            using (AssetsFileWriter w = new AssetsFileWriter(ms))
            {
                bun.Write(w);
                data = ms.ToArray();
            }
            Console.WriteLine($"{bundle} bundleari aldaketak aplikatzen...");

            foreach (Stream stream in streams)
                stream.Close();

            bun.Close();

            File.WriteAllBytes(bundle, data);

            Console.WriteLine("Eginda.");

        }


#if SDK
        private static void ExtractAssetAsJson(string[] args)
        {
            var assetsFilePath = args[1];
            var assetName = args[2];

            Console.WriteLine($"{assetName} baliabidea JSON formatuan erauzten...");

            var am = new AssetsManager();
            using var tpkStream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ItzulTool.ReleaseFiles.classdata.tpk");
            if (tpkStream == null)
            {
                Console.Error.WriteLine("Errorea: classdata.tpk ez da aurkitu exekutagarriaren barruan.");
                return;
            }
            am.LoadClassPackage(tpkStream);

            var assetsInst = am.LoadAssetsFile(assetsFilePath, false);

            string unityVersion = assetsInst.file.Metadata.UnityVersion;
            var cldb = am.LoadClassDatabaseFromPackage(unityVersion);
            if (cldb == null)
            {
                Console.Error.WriteLine($"Errorea: '{unityVersion}' bertsioarentzako klaseen datu-basea ez da classdata.tpk-n aurkitu.");
                return;
            }

            foreach (var assetInfo in assetsInst.file.AssetInfos)
            {
                AssetTypeValueField baseField;
                try { baseField = am.GetBaseField(assetsInst, assetInfo); }
                catch { continue; }

                if (baseField == null) continue;

                var nameField = baseField["m_Name"];
                if (nameField.IsDummy || nameField.AsString != assetName) continue;

                JToken jToken = FieldToJToken(baseField);
                string json = jToken.ToString(Formatting.Indented);

                string outputPath = assetName + ".json";
                File.WriteAllText(outputPath, json);
                Console.WriteLine($"{outputPath} idatzia.");
                return;
            }

            Console.WriteLine($"Ez da '{assetName}' izeneko baliabiderik aurkitu.");
        }

        private static JToken FieldToJToken(AssetTypeValueField field)
        {
            if (field.Value != null)
            {
                switch (field.Value.ValueType)
                {
                    case AssetValueType.String: return new JValue(field.AsString);
                    case AssetValueType.Bool: return new JValue(field.AsBool);
                    case AssetValueType.Int8:
                    case AssetValueType.Int16:
                    case AssetValueType.Int32: return new JValue(field.AsInt);
                    case AssetValueType.UInt8:
                    case AssetValueType.UInt16:
                    case AssetValueType.UInt32: return new JValue((long)(uint)field.AsInt);
                    case AssetValueType.Int64: return new JValue(field.AsLong);
                    case AssetValueType.UInt64: return new JValue(field.AsULong);
                    case AssetValueType.Float: return new JValue(field.AsFloat);
                    case AssetValueType.Double: return new JValue(field.AsDouble);
                    case AssetValueType.ByteArray: return new JValue(Convert.ToBase64String(field.AsByteArray));
                    case AssetValueType.Array:
                    {
                        var arr = new JArray();
                        foreach (var child in field.Children)
                            arr.Add(FieldToJToken(child));
                        return arr;
                    }
                }
            }

            if (field.TemplateField.IsArray)
            {
                var arr = new JArray();
                foreach (var child in field.Children)
                    arr.Add(FieldToJToken(child));
                return arr;
            }

            var obj = new JObject();
            foreach (var child in field.Children)
                obj[child.FieldName] = FieldToJToken(child);
            return obj;
        }

        private static void ConvertJsonToCsv(string[] args)
        {

            var jsonFile = args[1];
            var csvFile = args[2];
            string termsPath = args.Length > 3 ? args[3] : "mSource/mTerms/Array";
            string termIdPath = args.Length > 4 ? args[4] : "Term";
            string termLanguagesPath = args.Length > 5 ? args[5] : "Languages/Array";

            Console.WriteLine($"{jsonFile} JSON fitxategia CSVra bihurtzen...");

            string jsonContent = File.ReadAllText(@jsonFile);

            string csvContent = jsonToCsv(jsonContent, termsPath, termIdPath, termLanguagesPath);

            byte[] bytes = Encoding.UTF8.GetBytes(csvContent);
            File.WriteAllBytes(csvFile, bytes);

            Console.WriteLine("Eginda.");

        }

        private static string jsonToCsv(string jsonContent, string termsPath, string termIdPath, string termLanguagesPath)
        {
            StringWriter csvString = new StringWriter();
            using (var csv = new CsvWriter(csvString, CultureInfo.InvariantCulture))
            {
                JObject json = JObject.Parse(jsonContent);
                JArray a = (JArray) getTokenFromPath(json, termsPath);

                foreach(JObject t in a) {
                    string term =  getTokenFromPath(t, termIdPath).ToString();
                    csv.WriteField(term);
                    //string line = "\"" + term + "\",";
                    JArray translations = (JArray) getTokenFromPath(t, termLanguagesPath);
                    foreach(JValue l in translations) {
                        //line += "\"" + l.ToString() + "\",";
                        csv.WriteField(l.ToString());
                    }
                    csv.NextRecord();
                }
            }
            return csvString.ToString();
        }

        private static void UpdateJsonFromCsv(string[] args)
        {

            var jsonFile = args[1];
            var newJsonFile = jsonFile + ".new";
            var csvFile = args[2];
            string termsPath = args.Length > 3 ? args[3] : "mSource/mTerms/Array";
            string termIdPath = args.Length > 4 ? args[4] : "Term";
            string termLanguagesPath = args.Length > 5 ? args[5] : "Languages/Array";
            int languageIndex = args.Length > 6 ? Int32.Parse(args[6]) : -1;

            var replacements = new Dictionary<string, string>();

            Console.WriteLine($"{jsonFile} JSON fitxategia eguneratzen CSVko balioekin...");

            string jsonContent = File.ReadAllText(@jsonFile);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ",", HasHeaderRecord = false };
            using (var reader = new StreamReader(@csvFile))
            using (var csv = new CsvReader(reader , config))
            {
                while(csv.Read()) {
                    string id = csv.GetField(0);
                    string value = csv.GetField(languageIndex + 1);
                    replacements[id] = value;

                }
            }

            var updatedJsonContent = updateJson(jsonContent, replacements, termsPath, termIdPath, termLanguagesPath, languageIndex);
            byte[] bytes = Encoding.UTF8.GetBytes(updatedJsonContent);
            File.WriteAllBytes(newJsonFile, bytes);

            Console.WriteLine("Eginda.");

        }

        static string updateJson(string jsonContent, Dictionary<string, string> replacements, string termsPath, string termIdPath, string termLanguagesPath, int languagesIndex) {

            JObject json = JObject.Parse(jsonContent);
            JArray arr = (JArray) getTokenFromPath(json, termsPath);

            foreach(JObject row in arr.Children<JObject>()) {
                string id = getTokenFromPath(row, termIdPath).ToString();
                if(replacements.ContainsKey(id)) {
                    string val = replacements[id];
                    JArray translations = (JArray) getTokenFromPath(row, termLanguagesPath);
                    translations[languagesIndex] = val;
                }
            }
            
            return json.ToString();
        }

        static JToken getTokenFromPath(JObject json, string termsPath) {
            string[] tp = termsPath.Split("/");
            JToken aux = json;
            foreach(string key in tp) {
                aux = aux[key];
            }
            return aux;
        }
#endif


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
#if SDK
            else if (command == "extractasjson")
            {
                ExtractAssetAsJson(args);
            }
            else if (command == "jsontocsv")
            {
                ConvertJsonToCsv(args);
            }
            else if (command == "updatejsonfromcsv")
            {
                UpdateJsonFromCsv(args);
            }
#endif


            

        }
    }
}
