// Taksh Pathak
// CSE 445 - Assignment 4
using System;
using System.Xml.Schema;
using System.Xml;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ConsoleApp1
{
    public class Submission
    {
        // Public XML and schema files hosted in my GitHub repository.
        public static string xmlURL = "https://raw.githubusercontent.com/taksh1205/cse445-assignment4/main/NationalParks.xml";
        public static string xmlErrorURL = "https://raw.githubusercontent.com/taksh1205/cse445-assignment4/main/NationalParksErrors.xml";
        public static string xsdURL = "https://raw.githubusercontent.com/taksh1205/cse445-assignment4/main/NationalParks.xsd";

        public static void Main(string[] args)
        {
            string result = Verification(xmlURL, xsdURL);
            Console.WriteLine(result);
            result = Verification(xmlErrorURL, xsdURL);
            Console.WriteLine(result);
            result = Xml2Json(xmlURL);
            Console.WriteLine(result);
        }

        // Validate the supplied XML against the supplied schema, including other schemas.
        public static string Verification(string xmlUrl, string xsdUrl)
        {
            List<string> errors = new List<string>();
            try
            {
                string content = DownloadContent(xmlUrl);
                XmlSchemaSet schemas = new XmlSchemaSet();
                using (StringReader text = new StringReader(DownloadContent(xsdUrl)))
                using (XmlReader reader = XmlReader.Create(text, SafeSettings()))
                    schemas.Add(null, reader);
                schemas.Compile();
                bool malformed = Validate(content, schemas, errors);

                // A fatal syntax error stops an XML reader. Recover only a diagnostic
                // copy of this assignment's missing Address tag to expose later errors.
                string diagnostic = content;
                if (malformed)
                {
                    diagnostic = Regex.Replace(content,
                        @"(<Address\b[^>]*>(?:(?!</?Address\b|</NationalPark>).)*)(</NationalPark>)",
                        "$1</Address>$2", RegexOptions.Singleline);
                }
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                try
                {
                    document.LoadXml(diagnostic);
                    XmlElement root = document.DocumentElement;
                    // An undeclared root hides child validation errors. If the schema
                    // declares its plural form, inspect that root in a separate pass.
                    XmlQualifiedName plural = new XmlQualifiedName(root.LocalName + "s", root.NamespaceURI);
                    if (!schemas.GlobalElements.Contains(new XmlQualifiedName(root.LocalName, root.NamespaceURI))
                        && schemas.GlobalElements.Contains(plural))
                    {
                        XmlElement replacement = document.CreateElement(root.Prefix, root.LocalName + "s", root.NamespaceURI);
                        foreach (XmlAttribute attribute in root.Attributes)
                            replacement.Attributes.Append((XmlAttribute)attribute.CloneNode(true));
                        while (root.HasChildNodes) replacement.AppendChild(root.FirstChild);
                        document.ReplaceChild(replacement, root);
                        diagnostic = document.OuterXml;
                    }
                    if (diagnostic != content)
                    {
                        List<string> additional = new List<string>();
                        Validate(diagnostic, schemas, additional);
                        foreach (string message in additional)
                            errors.Add("Diagnostic pass: " + message);
                    }
                }
                catch (XmlException) { /* Original syntax error is already recorded. */ }
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
            return errors.Count == 0 ? "No errors are found" : string.Join(Environment.NewLine, errors);
        }

        // Keep external entity resolution disabled while reading untrusted XML.
        private static XmlReaderSettings SafeSettings()
        {
            return new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        }

        private static bool Validate(string content, XmlSchemaSet schemas, List<string> errors)
        {
            XmlReaderSettings settings = SafeSettings();
            settings.ValidationType = ValidationType.Schema;
            settings.Schemas = schemas;
            settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
            settings.ValidationEventHandler += delegate(object sender, ValidationEventArgs e)
            {
                errors.Add(e.Severity + ": " + e.Message + " (line " + e.Exception.LineNumber
                    + ", position " + e.Exception.LinePosition + ")");
            };
            try
            {
                using (StringReader text = new StringReader(content))
                using (XmlReader reader = XmlReader.Create(text, settings))
                    while (reader.Read()) { }
                return false;
            }
            catch (XmlException exception)
            {
                errors.Add("XML syntax error: " + exception.Message);
                return true;
            }
        }

        public static string Xml2Json(string xmlUrl)
        {
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            using (StringReader text = new StringReader(DownloadContent(xmlUrl)))
            using (XmlReader reader = XmlReader.Create(text, SafeSettings()))
                document.Load(reader);
            // XML declarations and schema-location metadata are not park data.
            for (int i = document.ChildNodes.Count - 1; i >= 0; i--)
                if (document.ChildNodes[i].NodeType == XmlNodeType.XmlDeclaration)
                    document.RemoveChild(document.ChildNodes[i]);
            RemoveSchemaMetadata(document.DocumentElement);
            // Explicit array annotations retain arrays even for a single park or phone.
            foreach (XmlNode node in document.SelectNodes("/NationalParks/NationalPark | /NationalParks/NationalPark/Phone"))
            {
                XmlAttribute array = document.CreateAttribute("json", "Array", "http://james.newtonking.com/projects/json");
                array.Value = "true";
                node.Attributes.Append(array);
            }
            return JsonConvert.SerializeXmlNode(document, Newtonsoft.Json.Formatting.Indented);
        }

        private static void RemoveSchemaMetadata(XmlElement element)
        {
            for (int i = element.Attributes.Count - 1; i >= 0; i--)
            {
                XmlAttribute attribute = element.Attributes[i];
                if (attribute.NamespaceURI == "http://www.w3.org/2001/XMLSchema-instance"
                    || (attribute.NamespaceURI == "http://www.w3.org/2000/xmlns/"
                        && attribute.Value == "http://www.w3.org/2001/XMLSchema-instance"))
                    element.Attributes.RemoveAt(i);
            }
            foreach (XmlNode child in element.ChildNodes)
                if (child is XmlElement) RemoveSchemaMetadata((XmlElement)child);
        }

        // The template's download helper accepts public URLs and local test files.
        private static string DownloadContent(string url)
        {
            Uri address;
            if (Uri.TryCreate(url, UriKind.Absolute, out address) && address.IsFile)
                return File.ReadAllText(address.LocalPath);
            if (File.Exists(url)) return File.ReadAllText(url);
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            using (System.Net.WebClient client = new System.Net.WebClient())
            {
                client.Encoding = System.Text.Encoding.UTF8;
                return client.DownloadString(url);
            }
        }
    }
}

