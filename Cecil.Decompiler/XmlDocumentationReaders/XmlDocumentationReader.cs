using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace Telerik.JustDecompiler.XmlDocumentationReaders
{
	class XmlDocumentationReader
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="XmlDocumentationReader" /> class.
		/// </summary>
		/// <param name="fileName">Name of the file that contains the documentation.</param>
		public XmlDocumentationReader()
		{
			//this.documentationMap = new Dictionary<string, string>();
		}

		/// <summary>
		/// Reads the documentation from the XML file provided via <paramref name="fileName"/>.
		/// </summary>
		/// <param name="fileName">Full path to the file containing the documentation.</param>
		public Dictionary<string,string> ReadDocumentation(string fileName)
		{
			XmlReaderSettings settings = new XmlReaderSettings();
			settings.IgnoreComments = true;
			settings.IgnoreWhitespace = true;
			settings.CloseInput = true;

			if (!File.Exists(fileName))
			{
				/// The file either doesn't exist, or JustDecompile doesn't have permissions to read it.
				return new Dictionary<string, string>();
			}

			using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
			{
				Dictionary<string, string> documentationMap = new Dictionary<string, string>();

				using (XmlTextReader reader = new XmlTextReader(fs, XmlNodeType.Element, null))
				{
					try
					{
						while (reader.Read())
						{
							if (reader.NodeType == XmlNodeType.Element)
							{
								if (reader.Name == "member")
								{
									string elementName = reader.GetAttribute("name"); // The assembly member to which the following section is related.
									string documentation = RemoveLeadingLineWhitespaces(reader.ReadInnerXml()); // The documenting section.

									// Keep only the documentation from the last encountered documentation tag. (similar to VS Intellisense)
									documentationMap[elementName] = documentation;									
								}
							}
						}
					}
					catch (XmlException)
					{ 
						// the XML file containing the documentation is corrupt. 
						return new Dictionary<string,string>();
					}
				}
				return documentationMap;
			}
		}

		private string RemoveLeadingLineWhitespaces(string text)
		{
			// Test the behavior of this method when dealing with files, where tabs and spaces are mixed together. Might produce some undesired formating.
			StringBuilder result = new StringBuilder();
			List<string> lines = new List<string>();
			int numberOfWhiteSpaces = int.MaxValue;
			using (StringReader sr = new StringReader(text))
			{
				string currentLine = sr.ReadLine();
				while (currentLine != null)
				{
					for (int i = 0; i < currentLine.Length; i++)
					{
						if (!char.IsWhiteSpace(currentLine[i]))
						{
							numberOfWhiteSpaces = Math.Min(numberOfWhiteSpaces, i);
							lines.Add(currentLine);
							break;
						}
					}
					currentLine = sr.ReadLine();
				}
			}
			foreach (string line in lines)
			{
				result.AppendLine(line.Substring(numberOfWhiteSpaces));
			}
			return result.ToString();
		}
	}
}
