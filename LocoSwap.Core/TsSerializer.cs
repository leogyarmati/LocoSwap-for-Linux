using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LocoSwap
{
    public static class TsSerializer
    {
        public static XDocument Load(string binPath)
        {
            return XmlDocumentLoader.Load(BinToXml(binPath));
        }

        public static string BinToXml(string binPath)
        {
            FileInfo binInfo = new FileInfo(binPath);
            string baseName = Path.GetFileNameWithoutExtension(binInfo.Name);
            string tempName = string.Format("{0}-{1}.bin", baseName, Utilities.StaticRandom.Instance.Next(10000, 99999));

            string tempBinPath;
            string tempXmlPath;

            if (!binPath.StartsWith(Utilities.GetTempDir()))
            {
                tempBinPath = Path.Combine(Utilities.GetTempDir(), tempName);
                tempXmlPath = Path.ChangeExtension(tempBinPath, "xml");
                Utilities.RemoveFile(tempBinPath);
                Utilities.RemoveFile(tempXmlPath);

                File.Copy(binPath, tempBinPath);
            }
            else
            {
                tempBinPath = binPath;
                tempXmlPath = Path.ChangeExtension(tempBinPath, "xml");
            }

            string output = SerzRunner.Run(tempBinPath);
            return EnsureProduced(tempXmlPath, output);
        }

        public static void Save(XDocument document, string path)
        {
            string xmlPath = Path.ChangeExtension(path, "xml");
            Utilities.RemoveFile(xmlPath);
            Utilities.RemoveFile(path);

            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings();
            xmlWriterSettings.Indent = true;
            xmlWriterSettings.IndentChars = "\t";
            xmlWriterSettings.Encoding = new UTF8Encoding(false);
            xmlWriterSettings.NewLineHandling = NewLineHandling.None;

            using (FileStream stream = new FileStream(xmlPath, FileMode.Create))
            using (XmlWriter writer = XmlWriter.Create(stream, xmlWriterSettings))
            {
                document.Save(writer);
            }

            string output = SerzRunner.Run(xmlPath);
            EnsureProduced(path, output);
        }

        // serz writes its result next to the input file. Make sure it really exists
        // (and tolerate a differently-cased file name coming out of Wine).
        private static string EnsureProduced(string expectedPath, string serzOutput)
        {
            string actual = Fs.ResolveCI(expectedPath);
            if (!File.Exists(actual))
            {
                throw new IOException(string.Format("serz.exe did not produce {0}. Output: {1}", expectedPath, serzOutput));
            }
            if (actual != expectedPath)
            {
                File.Move(actual, expectedPath, true);
            }
            return expectedPath;
        }
    }
}
