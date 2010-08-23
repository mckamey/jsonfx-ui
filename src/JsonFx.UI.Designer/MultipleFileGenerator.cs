#region License
/*---------------------------------------------------------------------------------*\

	Distributed under the terms of an MIT-style license:

	The MIT License

	Copyright (c) 2006-2010 Stephen M. McKamey

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.

\*---------------------------------------------------------------------------------*/
#endregion License

using System;
using System.Collections.Generic;
using System.IO;

using EnvDTE;

namespace JsonFx.UI.Designer
{
	public abstract class MultipleFileGenerator : BaseCodeGenerator
	{
		#region Fields

		private string extension;

		#endregion Fields

		#region MultipleFileGenerator Methods

		/// <summary>
		/// The method that does the actual work of generating code given the input file
		/// </summary>
		/// <param name="inputFileContent">File contents as a string</param>
		/// <returns>The generated code file as a byte-array</returns>
		protected abstract IEnumerable<FileGeneratorResult> GenerateFiles(string inputFileName, string defaultNamespace, string inputFileContent);

		#endregion MultipleFileGenerator Methods

		#region BaseCodeGenerator Methods

		protected sealed override string GetDefaultExtension()
		{
			return this.extension;
		}

		protected sealed override byte[] GenerateCode(string inputFileName, string defaultNamespace, string inputFileContent)
		{
			this.extension = null;

			var generatedFiles = this.GenerateFiles(inputFileName, defaultNamespace, inputFileContent);
			if (generatedFiles == null)
			{
				return null;
			}

			List<FileGeneratorResult> results =
				(generatedFiles is List<FileGeneratorResult>) ?
				(List<FileGeneratorResult>)generatedFiles :
				new List<FileGeneratorResult>(generatedFiles);
			ProjectItem item = this.GetProjectItem(inputFileName);

			// remove any existing child items
			foreach (ProjectItem child in item.ProjectItems)
			{
				if (results.FindIndex(n => StringComparer.OrdinalIgnoreCase.Equals(n.Name, child.Name)) < 0)
				{
					child.Delete();
				}
			}

			// save and add all the new child items
			for (int i=1; i<results.Count; i++)
			{
				var result = results[i];

				string filename = result.FullName;
				using (FileStream stream = File.Create(filename))
				{
					if (result.Content != null)
					{
						stream.Write(result.Content, 0, result.Content.Length);
					}
				}

				ProjectItem child = item.ProjectItems.AddFromFile(filename);
				if (!String.IsNullOrEmpty(result.CustomTool))
				{
					child.Properties.Item("CustomTool").Value = result.CustomTool;
				}

				if (result.BuildAction != 0)
				{
					child.Properties.Item("BuildAction").Value = result.BuildAction;
				}
			}

			if (results.Count < 1)
			{
				return null;
			}

			this.extension = results[0].Extension;
			return results[0].Content;
		}

		#endregion BaseCodeGenerator Methods
	}
}