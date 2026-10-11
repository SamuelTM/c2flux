using System;
using System.Collections.Generic;
using System.IO;

namespace c2flux
{
    // The file type categories of the analysis (Images, Video, ...): by
    // extension, plus a few backup and game data names. Returns the
    // localization key of the category.
    public static class FileTypeCategories
    {
        private static readonly Dictionary<string, string>
            CategoryByExtension =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
            [".jpg"] = "Advanced.FileType.Images",
            [".jpeg"] = "Advanced.FileType.Images",
            [".jpe"] = "Advanced.FileType.Images",
            [".png"] = "Advanced.FileType.Images",
            [".gif"] = "Advanced.FileType.Images",
            [".bmp"] = "Advanced.FileType.Images",
            [".tif"] = "Advanced.FileType.Images",
            [".tiff"] = "Advanced.FileType.Images",
            [".webp"] = "Advanced.FileType.Images",
            [".heic"] = "Advanced.FileType.Images",
            [".heif"] = "Advanced.FileType.Images",
            [".avif"] = "Advanced.FileType.Images",
            [".ico"] = "Advanced.FileType.Images",
            [".svg"] = "Advanced.FileType.Images",
            [".eps"] = "Advanced.FileType.Images",
            [".psd"] = "Advanced.FileType.Images",
            [".raw"] = "Advanced.FileType.Images",
            [".dng"] = "Advanced.FileType.Images",
            [".cr2"] = "Advanced.FileType.Images",
            [".cr3"] = "Advanced.FileType.Images",
            [".nef"] = "Advanced.FileType.Images",
            [".arw"] = "Advanced.FileType.Images",
            [".orf"] = "Advanced.FileType.Images",
            [".rw2"] = "Advanced.FileType.Images",
            [".jxr"] = "Advanced.FileType.Images",
            [".wdp"] = "Advanced.FileType.Images",
            [".dds"] = "Advanced.FileType.Images",
            [".jfif"] = "Advanced.FileType.Images",
            [".jp2"] = "Advanced.FileType.Images",
            [".j2k"] = "Advanced.FileType.Images",
            [".jpf"] = "Advanced.FileType.Images",
            [".jpx"] = "Advanced.FileType.Images",
            [".psb"] = "Advanced.FileType.Images",
            [".ai"] = "Advanced.FileType.Images",
            [".emf"] = "Advanced.FileType.Images",
            [".wmf"] = "Advanced.FileType.Images",
            [".pcx"] = "Advanced.FileType.Images",
            [".tga"] = "Advanced.FileType.Images",
            [".hdr"] = "Advanced.FileType.Images",
            [".exr"] = "Advanced.FileType.Images",
            [".xcf"] = "Advanced.FileType.Images",
            [".kra"] = "Advanced.FileType.Images",
            [".icns"] = "Advanced.FileType.Images",
            [".pbm"] = "Advanced.FileType.Images",
            [".pgm"] = "Advanced.FileType.Images",
            [".ppm"] = "Advanced.FileType.Images",
            [".pnm"] = "Advanced.FileType.Images",
            [".mp4"] = "Advanced.FileType.Video",
            [".m4v"] = "Advanced.FileType.Video",
            [".mkv"] = "Advanced.FileType.Video",
            [".avi"] = "Advanced.FileType.Video",
            [".mov"] = "Advanced.FileType.Video",
            [".wmv"] = "Advanced.FileType.Video",
            [".flv"] = "Advanced.FileType.Video",
            [".webm"] = "Advanced.FileType.Video",
            [".mpeg"] = "Advanced.FileType.Video",
            [".mpg"] = "Advanced.FileType.Video",
            [".mpe"] = "Advanced.FileType.Video",
            [".m2v"] = "Advanced.FileType.Video",
            [".mts"] = "Advanced.FileType.Video",
            [".m2ts"] = "Advanced.FileType.Video",
            [".vob"] = "Advanced.FileType.Video",
            [".3gp"] = "Advanced.FileType.Video",
            [".3g2"] = "Advanced.FileType.Video",
            [".ogv"] = "Advanced.FileType.Video",
            [".asf"] = "Advanced.FileType.Video",
            [".bk2"] = "Advanced.FileType.Video",
            [".bik"] = "Advanced.FileType.Video",
            [".f4v"] = "Advanced.FileType.Video",
            [".mp4v"] = "Advanced.FileType.Video",
            [".qt"] = "Advanced.FileType.Video",
            [".divx"] = "Advanced.FileType.Video",
            [".mxf"] = "Advanced.FileType.Video",
            [".rm"] = "Advanced.FileType.Video",
            [".rmvb"] = "Advanced.FileType.Video",
            [".dv"] = "Advanced.FileType.Video",
            [".mp3"] = "Advanced.FileType.Audio",
            [".wav"] = "Advanced.FileType.Audio",
            [".flac"] = "Advanced.FileType.Audio",
            [".aac"] = "Advanced.FileType.Audio",
            [".m4a"] = "Advanced.FileType.Audio",
            [".ogg"] = "Advanced.FileType.Audio",
            [".oga"] = "Advanced.FileType.Audio",
            [".opus"] = "Advanced.FileType.Audio",
            [".wma"] = "Advanced.FileType.Audio",
            [".aif"] = "Advanced.FileType.Audio",
            [".aiff"] = "Advanced.FileType.Audio",
            [".aifc"] = "Advanced.FileType.Audio",
            [".mid"] = "Advanced.FileType.Audio",
            [".midi"] = "Advanced.FileType.Audio",
            [".cda"] = "Advanced.FileType.Audio",
            [".ape"] = "Advanced.FileType.Audio",
            [".ac3"] = "Advanced.FileType.Audio",
            [".mka"] = "Advanced.FileType.Audio",
            [".wem"] = "Advanced.FileType.Audio",
            [".bnk"] = "Advanced.FileType.Audio",
            [".xwm"] = "Advanced.FileType.Audio",
            [".fuz"] = "Advanced.FileType.Audio",
            [".m4b"] = "Advanced.FileType.Audio",
            [".amr"] = "Advanced.FileType.Audio",
            [".au"] = "Advanced.FileType.Audio",
            [".snd"] = "Advanced.FileType.Audio",
            [".wv"] = "Advanced.FileType.Audio",
            [".tta"] = "Advanced.FileType.Audio",
            [".dsf"] = "Advanced.FileType.Audio",
            [".dff"] = "Advanced.FileType.Audio",
            [".caf"] = "Advanced.FileType.Audio",
            [".m3u"] = "Advanced.FileType.Audio",
            [".m3u8"] = "Advanced.FileType.Audio",
            [".pls"] = "Advanced.FileType.Audio",
            [".txt"] = "Advanced.FileType.Documents",
            [".rtf"] = "Advanced.FileType.Documents",
            [".pdf"] = "Advanced.FileType.Documents",
            [".xps"] = "Advanced.FileType.Documents",
            [".oxps"] = "Advanced.FileType.Documents",
            [".doc"] = "Advanced.FileType.Documents",
            [".docx"] = "Advanced.FileType.Documents",
            [".docm"] = "Advanced.FileType.Documents",
            [".dot"] = "Advanced.FileType.Documents",
            [".dotx"] = "Advanced.FileType.Documents",
            [".dotm"] = "Advanced.FileType.Documents",
            [".xls"] = "Advanced.FileType.Documents",
            [".xlsx"] = "Advanced.FileType.Documents",
            [".xlsm"] = "Advanced.FileType.Documents",
            [".xlsb"] = "Advanced.FileType.Documents",
            [".xlt"] = "Advanced.FileType.Documents",
            [".xltx"] = "Advanced.FileType.Documents",
            [".xltm"] = "Advanced.FileType.Documents",
            [".csv"] = "Advanced.FileType.Documents",
            [".ods"] = "Advanced.FileType.Documents",
            [".ppt"] = "Advanced.FileType.Documents",
            [".pptx"] = "Advanced.FileType.Documents",
            [".pptm"] = "Advanced.FileType.Documents",
            [".pps"] = "Advanced.FileType.Documents",
            [".ppsx"] = "Advanced.FileType.Documents",
            [".ppsm"] = "Advanced.FileType.Documents",
            [".pot"] = "Advanced.FileType.Documents",
            [".potx"] = "Advanced.FileType.Documents",
            [".potm"] = "Advanced.FileType.Documents",
            [".odp"] = "Advanced.FileType.Documents",
            [".odt"] = "Advanced.FileType.Documents",
            [".pages"] = "Advanced.FileType.Documents",
            [".numbers"] = "Advanced.FileType.Documents",
            [".key"] = "Advanced.FileType.Documents",
            [".pub"] = "Advanced.FileType.Documents",
            [".wpd"] = "Advanced.FileType.Documents",
            [".epub"] = "Advanced.FileType.Documents",
            [".mobi"] = "Advanced.FileType.Documents",
            [".vsd"] = "Advanced.FileType.Documents",
            [".vsdx"] = "Advanced.FileType.Documents",
            [".vsdm"] = "Advanced.FileType.Documents",
            [".eml"] = "Advanced.FileType.Documents",
            [".msg"] = "Advanced.FileType.Documents",
            [".one"] = "Advanced.FileType.Documents",
            [".onetoc2"] = "Advanced.FileType.Documents",
            [".ps"] = "Advanced.FileType.Documents",
            [".djvu"] = "Advanced.FileType.Documents",
            [".djv"] = "Advanced.FileType.Documents",
            [".indd"] = "Advanced.FileType.Documents",
            [".indt"] = "Advanced.FileType.Documents",
            [".idml"] = "Advanced.FileType.Documents",
            [".indb"] = "Advanced.FileType.Documents",
            [".indl"] = "Advanced.FileType.Documents",
            [".icml"] = "Advanced.FileType.Documents",
            [".odg"] = "Advanced.FileType.Documents",
            [".ott"] = "Advanced.FileType.Documents",
            [".ots"] = "Advanced.FileType.Documents",
            [".otp"] = "Advanced.FileType.Documents",
            [".zip"] = "Advanced.FileType.Archives",
            [".7z"] = "Advanced.FileType.Archives",
            [".rar"] = "Advanced.FileType.Archives",
            [".tar"] = "Advanced.FileType.Archives",
            [".gz"] = "Advanced.FileType.Archives",
            [".gzip"] = "Advanced.FileType.Archives",
            [".bz2"] = "Advanced.FileType.Archives",
            [".xz"] = "Advanced.FileType.Archives",
            [".zst"] = "Advanced.FileType.Archives",
            [".tgz"] = "Advanced.FileType.Archives",
            [".tbz"] = "Advanced.FileType.Archives",
            [".tbz2"] = "Advanced.FileType.Archives",
            [".txz"] = "Advanced.FileType.Archives",
            [".cab"] = "Advanced.FileType.Archives",
            [".arj"] = "Advanced.FileType.Archives",
            [".lha"] = "Advanced.FileType.Archives",
            [".lzh"] = "Advanced.FileType.Archives",
            [".ace"] = "Advanced.FileType.Archives",
            [".zipx"] = "Advanced.FileType.Archives",
            [".lz"] = "Advanced.FileType.Archives",
            [".lzma"] = "Advanced.FileType.Archives",
            [".lz4"] = "Advanced.FileType.Archives",
            [".lzo"] = "Advanced.FileType.Archives",
            [".br"] = "Advanced.FileType.Archives",
            [".z"] = "Advanced.FileType.Archives",
            [".cpio"] = "Advanced.FileType.Archives",
            [".zoo"] = "Advanced.FileType.Archives",
            [".sit"] = "Advanced.FileType.Archives",
            [".sitx"] = "Advanced.FileType.Archives",
            [".pak"] = "Advanced.FileType.GameFiles",
            [".ucas"] = "Advanced.FileType.GameFiles",
            [".utoc"] = "Advanced.FileType.GameFiles",
            [".uasset"] = "Advanced.FileType.GameFiles",
            [".uexp"] = "Advanced.FileType.GameFiles",
            [".ubulk"] = "Advanced.FileType.GameFiles",
            [".upk"] = "Advanced.FileType.GameFiles",
            [".assets"] = "Advanced.FileType.GameFiles",
            [".ress"] = "Advanced.FileType.GameFiles",
            [".resource"] = "Advanced.FileType.GameFiles",
            [".forge"] = "Advanced.FileType.GameFiles",
            [".bsa"] = "Advanced.FileType.GameFiles",
            [".ba2"] = "Advanced.FileType.GameFiles",
            [".rpf"] = "Advanced.FileType.GameFiles",
            [".vpk"] = "Advanced.FileType.GameFiles",
            [".pck"] = "Advanced.FileType.GameFiles",
            [".cok"] = "Advanced.FileType.GameFiles",
            [".cas"] = "Advanced.FileType.GameFiles",
            [".bundle"] = "Advanced.FileType.GameFiles",
            [".nxa"] = "Advanced.FileType.GameFiles",
            [".mwm"] = "Advanced.FileType.GameFiles",
            [".tbf"] = "Advanced.FileType.GameFiles",
            [".rda"] = "Advanced.FileType.GameFiles",
            [".kfc"] = "Advanced.FileType.GameFiles",
            [".kfc_resources"] = "Advanced.FileType.GameFiles",
            [".acf"] = "Advanced.FileType.GameFiles",
            [".vdf"] = "Advanced.FileType.GameFiles",
            [".sav"] = "Advanced.FileType.GameFiles",
            [".save"] = "Advanced.FileType.GameFiles",
            [".umap"] = "Advanced.FileType.GameFiles",
            [".locres"] = "Advanced.FileType.GameFiles",
            [".ushaderbytecode"] = "Advanced.FileType.GameFiles",
            [".nif"] = "Advanced.FileType.GameFiles",
            [".esp"] = "Advanced.FileType.GameFiles",
            [".esm"] = "Advanced.FileType.GameFiles",
            [".esl"] = "Advanced.FileType.GameFiles",
            [".pex"] = "Advanced.FileType.GameFiles",
            [".psc"] = "Advanced.FileType.GameFiles",
            [".awc"] = "Advanced.FileType.GameFiles",
            [".ytd"] = "Advanced.FileType.GameFiles",
            [".ydr"] = "Advanced.FileType.GameFiles",
            [".yft"] = "Advanced.FileType.GameFiles",
            [".ymap"] = "Advanced.FileType.GameFiles",
            [".ytyp"] = "Advanced.FileType.GameFiles",
            [".ysc"] = "Advanced.FileType.GameFiles",
            [".bsp"] = "Advanced.FileType.GameFiles",
            [".vtx"] = "Advanced.FileType.GameFiles",
            [".vvd"] = "Advanced.FileType.GameFiles",
            [".mdl"] = "Advanced.FileType.GameFiles",
            [".phy"] = "Advanced.FileType.GameFiles",
            [".nav"] = "Advanced.FileType.GameFiles",
            [".pk3"] = "Advanced.FileType.GameFiles",
            [".pk4"] = "Advanced.FileType.GameFiles",
            [".wad"] = "Advanced.FileType.GameFiles",
            [".iwd"] = "Advanced.FileType.GameFiles",
            [".xpak"] = "Advanced.FileType.GameFiles",
            [".ff"] = "Advanced.FileType.GameFiles",
            [".exe"] = "Advanced.FileType.Applications",
            [".com"] = "Advanced.FileType.Applications",
            [".msi"] = "Advanced.FileType.Applications",
            [".msix"] = "Advanced.FileType.Applications",
            [".msixbundle"] = "Advanced.FileType.Applications",
            [".appx"] = "Advanced.FileType.Applications",
            [".appxbundle"] = "Advanced.FileType.Applications",
            [".dll"] = "Advanced.FileType.Applications",
            [".ocx"] = "Advanced.FileType.Applications",
            [".cpl"] = "Advanced.FileType.Applications",
            [".scr"] = "Advanced.FileType.Applications",
            [".jar"] = "Advanced.FileType.Applications",
            [".war"] = "Advanced.FileType.Applications",
            [".apk"] = "Advanced.FileType.Applications",
            [".aab"] = "Advanced.FileType.Applications",
            [".appinstaller"] = "Advanced.FileType.Applications",
            [".msu"] = "Advanced.FileType.Applications",
            [".msp"] = "Advanced.FileType.Applications",
            [".mst"] = "Advanced.FileType.Applications",
            [".deb"] = "Advanced.FileType.Applications",
            [".rpm"] = "Advanced.FileType.Applications",
            [".sys"] = "Advanced.FileType.SystemFiles",
            [".drv"] = "Advanced.FileType.SystemFiles",
            [".mui"] = "Advanced.FileType.SystemFiles",
            [".efi"] = "Advanced.FileType.SystemFiles",
            [".cat"] = "Advanced.FileType.SystemFiles",
            [".manifest"] = "Advanced.FileType.SystemFiles",
            [".nls"] = "Advanced.FileType.SystemFiles",
            [".inf"] = "Advanced.FileType.SystemFiles",
            [".admx"] = "Advanced.FileType.SystemFiles",
            [".adml"] = "Advanced.FileType.SystemFiles",
            [".pol"] = "Advanced.FileType.SystemFiles",
            [".cs"] = "Advanced.FileType.Development",
            [".csproj"] = "Advanced.FileType.Development",
            [".sln"] = "Advanced.FileType.Development",
            [".slnx"] = "Advanced.FileType.Development",
            [".c"] = "Advanced.FileType.Development",
            [".cc"] = "Advanced.FileType.Development",
            [".cpp"] = "Advanced.FileType.Development",
            [".cxx"] = "Advanced.FileType.Development",
            [".h"] = "Advanced.FileType.Development",
            [".hh"] = "Advanced.FileType.Development",
            [".hpp"] = "Advanced.FileType.Development",
            [".java"] = "Advanced.FileType.Development",
            [".class"] = "Advanced.FileType.Development",
            [".kt"] = "Advanced.FileType.Development",
            [".kts"] = "Advanced.FileType.Development",
            [".py"] = "Advanced.FileType.Development",
            [".pyw"] = "Advanced.FileType.Development",
            [".pyc"] = "Advanced.FileType.Development",
            [".js"] = "Advanced.FileType.Development",
            [".jsx"] = "Advanced.FileType.Development",
            [".tsx"] = "Advanced.FileType.Development",
            [".html"] = "Advanced.FileType.Development",
            [".htm"] = "Advanced.FileType.Development",
            [".css"] = "Advanced.FileType.Development",
            [".scss"] = "Advanced.FileType.Development",
            [".sass"] = "Advanced.FileType.Development",
            [".less"] = "Advanced.FileType.Development",
            [".php"] = "Advanced.FileType.Development",
            [".rb"] = "Advanced.FileType.Development",
            [".go"] = "Advanced.FileType.Development",
            [".rs"] = "Advanced.FileType.Development",
            [".swift"] = "Advanced.FileType.Development",
            [".vb"] = "Advanced.FileType.Development",
            [".fs"] = "Advanced.FileType.Development",
            [".fsx"] = "Advanced.FileType.Development",
            [".ps1"] = "Advanced.FileType.Development",
            [".psm1"] = "Advanced.FileType.Development",
            [".psd1"] = "Advanced.FileType.Development",
            [".bat"] = "Advanced.FileType.Development",
            [".cmd"] = "Advanced.FileType.Development",
            [".sh"] = "Advanced.FileType.Development",
            [".xml"] = "Advanced.FileType.Development",
            [".json"] = "Advanced.FileType.Development",
            [".yaml"] = "Advanced.FileType.Development",
            [".yml"] = "Advanced.FileType.Development",
            [".toml"] = "Advanced.FileType.Development",
            [".sql"] = "Advanced.FileType.Development",
            [".props"] = "Advanced.FileType.Development",
            [".targets"] = "Advanced.FileType.Development",
            [".vcxproj"] = "Advanced.FileType.Development",
            [".fsproj"] = "Advanced.FileType.Development",
            [".vbproj"] = "Advanced.FileType.Development",
            [".gradle"] = "Advanced.FileType.Development",
            [".vue"] = "Advanced.FileType.Development",
            [".svelte"] = "Advanced.FileType.Development",
            [".md"] = "Advanced.FileType.Development",
            [".lua"] = "Advanced.FileType.Development",
            [".pl"] = "Advanced.FileType.Development",
            [".dart"] = "Advanced.FileType.Development",
            [".scala"] = "Advanced.FileType.Development",
            [".groovy"] = "Advanced.FileType.Development",
            [".ipynb"] = "Advanced.FileType.Development",
            [".asm"] = "Advanced.FileType.Development",
            [".inc"] = "Advanced.FileType.Development",
            [".proto"] = "Advanced.FileType.Development",
            [".graphql"] = "Advanced.FileType.Development",
            [".gql"] = "Advanced.FileType.Development",
            [".xaml"] = "Advanced.FileType.Development",
            [".resx"] = "Advanced.FileType.Development",
            [".razor"] = "Advanced.FileType.Development",
            [".cshtml"] = "Advanced.FileType.Development",
            [".vbhtml"] = "Advanced.FileType.Development",
            [".cmake"] = "Advanced.FileType.Development",
            [".wasm"] = "Advanced.FileType.Development",
            [".db"] = "Advanced.FileType.Databases",
            [".db3"] = "Advanced.FileType.Databases",
            [".sqlite"] = "Advanced.FileType.Databases",
            [".sqlite3"] = "Advanced.FileType.Databases",
            [".sqlitedb"] = "Advanced.FileType.Databases",
            [".mdb"] = "Advanced.FileType.Databases",
            [".accdb"] = "Advanced.FileType.Databases",
            [".accde"] = "Advanced.FileType.Databases",
            [".accdr"] = "Advanced.FileType.Databases",
            [".ndf"] = "Advanced.FileType.Databases",
            [".ldf"] = "Advanced.FileType.Databases",
            [".fdb"] = "Advanced.FileType.Databases",
            [".gdb"] = "Advanced.FileType.Databases",
            [".dbf"] = "Advanced.FileType.Databases",
            [".sdf"] = "Advanced.FileType.Databases",
            [".realm"] = "Advanced.FileType.Databases",
            [".odb"] = "Advanced.FileType.Databases",
            [".db-wal"] = "Advanced.FileType.Databases",
            [".db-shm"] = "Advanced.FileType.Databases",
            [".db-journal"] = "Advanced.FileType.Databases",
            [".sqlite-wal"] = "Advanced.FileType.Databases",
            [".sqlite-shm"] = "Advanced.FileType.Databases",
            [".sqlite-journal"] = "Advanced.FileType.Databases",
            [".sqlite3-wal"] = "Advanced.FileType.Databases",
            [".sqlite3-shm"] = "Advanced.FileType.Databases",
            [".sqlite3-journal"] = "Advanced.FileType.Databases",
            [".iso"] = "Advanced.FileType.DiskImages",
            [".img"] = "Advanced.FileType.DiskImages",
            [".ima"] = "Advanced.FileType.DiskImages",
            [".cue"] = "Advanced.FileType.DiskImages",
            [".nrg"] = "Advanced.FileType.DiskImages",
            [".dmg"] = "Advanced.FileType.DiskImages",
            [".vhd"] = "Advanced.FileType.DiskImages",
            [".vhdx"] = "Advanced.FileType.DiskImages",
            [".vmdk"] = "Advanced.FileType.DiskImages",
            [".vdi"] = "Advanced.FileType.DiskImages",
            [".qcow"] = "Advanced.FileType.DiskImages",
            [".qcow2"] = "Advanced.FileType.DiskImages",
            [".wim"] = "Advanced.FileType.DiskImages",
            [".esd"] = "Advanced.FileType.DiskImages",
            [".swm"] = "Advanced.FileType.DiskImages",
            [".ova"] = "Advanced.FileType.DiskImages",
            [".ovf"] = "Advanced.FileType.DiskImages",
            [".vhdset"] = "Advanced.FileType.DiskImages",
            [".bak"] = "Advanced.FileType.Backups",
            [".backup"] = "Advanced.FileType.Backups",
            [".bkf"] = "Advanced.FileType.Backups",
            [".bkp"] = "Advanced.FileType.Backups",
            [".old"] = "Advanced.FileType.Backups",
            [".wbk"] = "Advanced.FileType.Backups",
            [".tib"] = "Advanced.FileType.Backups",
            [".tibx"] = "Advanced.FileType.Backups",
            [".mrimg"] = "Advanced.FileType.Backups",
            [".vbk"] = "Advanced.FileType.Backups",
            [".vib"] = "Advanced.FileType.Backups",
            [".vrb"] = "Advanced.FileType.Backups",
            [".abk"] = "Advanced.FileType.Backups",
            [".vbm"] = "Advanced.FileType.Backups",
            [".vma"] = "Advanced.FileType.Backups",
            [".pxar"] = "Advanced.FileType.Backups",
            [".log"] = "Advanced.FileType.LogFiles",
            [".log1"] = "Advanced.FileType.LogFiles",
            [".log2"] = "Advanced.FileType.LogFiles",
            [".evtx"] = "Advanced.FileType.LogFiles",
            [".evt"] = "Advanced.FileType.LogFiles",
            [".etl"] = "Advanced.FileType.LogFiles",
            [".trace"] = "Advanced.FileType.LogFiles",
            [".trc"] = "Advanced.FileType.LogFiles",
            [".out"] = "Advanced.FileType.LogFiles",
            [".err"] = "Advanced.FileType.LogFiles",
            [".debug"] = "Advanced.FileType.LogFiles",
            [".dmp"] = "Advanced.FileType.LogFiles",
            [".mdmp"] = "Advanced.FileType.LogFiles",
            [".journal"] = "Advanced.FileType.LogFiles",
            [".tmp"] = "Advanced.FileType.TemporaryFiles",
            [".temp"] = "Advanced.FileType.TemporaryFiles",
            [".crdownload"] = "Advanced.FileType.TemporaryFiles",
            [".part"] = "Advanced.FileType.TemporaryFiles",
            [".partial"] = "Advanced.FileType.TemporaryFiles",
            [".download"] = "Advanced.FileType.TemporaryFiles",
            [".cache"] = "Advanced.FileType.TemporaryFiles",
            [".swp"] = "Advanced.FileType.TemporaryFiles",
            [".swo"] = "Advanced.FileType.TemporaryFiles",
                };

        public static string GetCategoryKey(
            string fileName)
        {
            if (IsKnownBackupFileName(fileName))
            {
                return "Advanced.FileType.Backups";
            }

            if (IsKnownGameDataFileName(fileName))
            {
                return "Advanced.FileType.GameFiles";
            }

            string extension = Path.GetExtension(fileName);

            if (!string.IsNullOrWhiteSpace(extension) &&
                CategoryByExtension.TryGetValue(
                    extension,
                    out string categoryKey))
            {
                return categoryKey;
            }

            return "Advanced.FileType.OtherFiles";
        }

        private static bool IsKnownBackupFileName(
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.StartsWith(
                    "vzdump-",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return
                fileName.EndsWith(
                    ".tar",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".tar.zst",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".tar.gz",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".tar.lzo",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".vma",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".vma.zst",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".vma.gz",
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(
                    ".vma.lzo",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownGameDataFileName(
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (fileName.Length == 8 &&
                fileName.StartsWith(
                    "data.",
                    StringComparison.OrdinalIgnoreCase) &&
                char.IsDigit(fileName[5]) &&
                char.IsDigit(fileName[6]) &&
                char.IsDigit(fileName[7]))
            {
                return true;
            }

            const string EnshroudedPrefix = "enshrouded_";
            const string DatExtension = ".dat";

            if (!fileName.StartsWith(
                    EnshroudedPrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(
                    DatExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int numberStart = EnshroudedPrefix.Length;
            int numberLength =
                fileName.Length -
                EnshroudedPrefix.Length -
                DatExtension.Length;

            return
                numberLength == 3 &&
                char.IsDigit(fileName[numberStart]) &&
                char.IsDigit(fileName[numberStart + 1]) &&
                char.IsDigit(fileName[numberStart + 2]);
        }
    }
}
