using System;
using System.Collections.Generic;
using System.Text;
using AssetsTools.NET;

namespace ItzulTool
{
    public class EmipAssetReplacer
    {
        public bool IsRemover;
        public long PathId;
        public int ClassId;
        public ushort MonoScriptIndex;
        public byte[] Data; // null if IsRemover
    }

    public class EmipBundleEntryReplacer
    {
        public string OldName;
        public string NewName;
        public bool HasSerializedData;
        public List<EmipAssetReplacer> AssetReplacers;
    }

    public class InstallerPackageFile
    {
        public string magic;
        public bool includesCldb;
        public string modName;
        public string modCreators;
        public string modDescription;
        public ClassDatabaseFile addedTypes;
        public List<InstallerPackageAssetsDesc> affectedFiles;

        public bool Read(AssetsFileReader reader, bool prefReplacersInMemory = false)
        {
            reader.BigEndian = false;

            magic = reader.ReadStringLength(4);
            if (magic != "EMIP")
                return false;

            includesCldb = reader.ReadByte() != 0;

            modName = reader.ReadCountStringInt16();
            modCreators = reader.ReadCountStringInt16();
            modDescription = reader.ReadCountStringInt16();

            if (includesCldb)
            {
                addedTypes = new ClassDatabaseFile();
                addedTypes.Read(reader);
            }
            else
            {
                addedTypes = null;
            }

            int affectedFilesCount = reader.ReadInt32();
            affectedFiles = new List<InstallerPackageAssetsDesc>();
            for (int i = 0; i < affectedFilesCount; i++)
            {
                InstallerPackageAssetsDesc desc = new InstallerPackageAssetsDesc()
                {
                    isBundle = reader.ReadByte() != 0,
                    path = reader.ReadCountStringInt16()
                };
                int replacerCount = reader.ReadInt32();
                if (desc.isBundle)
                {
                    desc.bundleReplacers = new List<EmipBundleEntryReplacer>();
                    for (int j = 0; j < replacerCount; j++)
                    {
                        var rep = ParseBundleReplacer(reader, prefReplacersInMemory);
                        if (rep != null)
                            desc.bundleReplacers.Add(rep);
                    }
                }
                else
                {
                    desc.assetReplacers = new List<EmipAssetReplacer>();
                    for (int j = 0; j < replacerCount; j++)
                    {
                        var rep = ParseAssetReplacer(reader, prefReplacersInMemory);
                        if (rep != null)
                            desc.assetReplacers.Add(rep);
                    }
                }
                affectedFiles.Add(desc);
            }

            return true;
        }

        public void Write(AssetsFileWriter writer)
        {
            throw new NotImplementedException("EMIP write is not supported in this version.");
        }

        private static EmipBundleEntryReplacer ParseBundleReplacer(AssetsFileReader reader, bool prefReplacersInMemory)
        {
            short replacerType = reader.ReadInt16();
            byte fileType = reader.ReadByte();
            if (fileType != 0) //not a BundleReplacer
                return null;

            string oldName = reader.ReadCountStringInt16();
            string newName = reader.ReadCountStringInt16();
            bool hasSerializedData = reader.ReadByte() != 0;
            long assetReplacerCount = reader.ReadInt64();

            var assetReplacers = new List<EmipAssetReplacer>();
            for (int i = 0; i < assetReplacerCount; i++)
            {
                var rep = ParseAssetReplacer(reader, prefReplacersInMemory);
                if (rep != null)
                    assetReplacers.Add(rep);
            }

            return new EmipBundleEntryReplacer
            {
                OldName = oldName,
                NewName = newName,
                HasSerializedData = hasSerializedData,
                AssetReplacers = assetReplacers
            };
        }

        private static EmipAssetReplacer ParseAssetReplacer(AssetsFileReader reader, bool prefReplacersInMemory)
        {
            short replacerType = reader.ReadInt16();
            byte fileType = reader.ReadByte();
            if (fileType != 1) //not an AssetsReplacer
                return null;

            byte unknown01 = reader.ReadByte(); //always 1
            int fileId = reader.ReadInt32();
            long pathId = reader.ReadInt64();
            int classId = reader.ReadInt32();
            ushort monoScriptIndex = reader.ReadUInt16();

            int preloadDependencyCount = reader.ReadInt32();
            for (int i = 0; i < preloadDependencyCount; i++)
            {
                reader.ReadInt32(); // fileId
                reader.ReadInt64(); // pathId
            }

            if (replacerType == 0) //remover
            {
                return new EmipAssetReplacer
                {
                    IsRemover = true,
                    PathId = pathId,
                    ClassId = classId,
                    MonoScriptIndex = monoScriptIndex
                };
            }
            else if (replacerType == 2) //adder/replacer
            {
                bool flag1 = reader.ReadByte() != 0;
                if (flag1)
                {
                    throw new NotSupportedException("you just found a file with the mysterious flag1 set, send the file to nes");
                }

                bool flag2 = reader.ReadByte() != 0; //has properties hash
                if (flag2)
                {
                    reader.ReadBytes(16); // Hash128 - read and discard
                }

                bool flag3 = reader.ReadByte() != 0; //has script hash
                if (flag3)
                {
                    reader.ReadBytes(16); // Hash128 - read and discard
                }

                bool flag4 = reader.ReadByte() != 0; //has cldb
                if (flag4)
                {
                    var classData = new ClassDatabaseFile();
                    classData.Read(reader); // read to advance stream position
                }

                long bufLength = reader.ReadInt64();
                byte[] buf;
                if (prefReplacersInMemory)
                {
                    buf = reader.ReadBytes((int)bufLength);
                }
                else
                {
                    buf = reader.ReadBytes((int)bufLength);
                }

                return new EmipAssetReplacer
                {
                    IsRemover = false,
                    PathId = pathId,
                    ClassId = classId,
                    MonoScriptIndex = monoScriptIndex,
                    Data = buf
                };
            }

            return null;
        }
    }

    public class InstallerPackageAssetsDesc
    {
        public bool isBundle;
        public string path;
        public List<EmipBundleEntryReplacer> bundleReplacers; // used when isBundle
        public List<EmipAssetReplacer> assetReplacers;        // used when !isBundle
    }
}
