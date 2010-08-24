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

			if (results.Count < 1)
			{
				// remove all
				foreach (ProjectItem child in item.ProjectItems)
				{
					child.Delete();
				}

				return null;
			}

			// find same last gen as VS will try to rename if we move these around
			string lastGenName = item.Properties.Item("CustomToolOutput").Value as string;
			int lastGenIndex = results.FindIndex(n => StringComparer.OrdinalIgnoreCase.Equals(n.Name, lastGenName));
			if (lastGenIndex < 0)
			{
				lastGenIndex = 0;
			}

			foreach (ProjectItem child in item.ProjectItems)
			{
				int index = results.FindIndex(n => StringComparer.OrdinalIgnoreCase.Equals(n.Name, child.Name));
				if (index < 0)
				{
					// remove extraneous child items
					child.Delete();
					continue;
				}

				var result = results[index];

				if (index != lastGenIndex)
				{
					// the actual data for first will get saved by base
					this.SaveFile(result);
				}

				this.SetItemProperties(result, child);
				result.Saved = true;
			}

			// save and add new child items (skipping first)
			for (int i=0; i<results.Count; i++)
			{
				if (i == lastGenIndex)
				{
					continue;
				}

				var result = results[i];
				if (result.Saved)
				{
					continue;
				}

				this.SaveFile(result);
				ProjectItem child = item.ProjectItems.AddFromFile(result.FullName);
				this.SetItemProperties(result, child);
			}

			var genOutput = results[lastGenIndex];
			this.extension = genOutput.Extension;
			return genOutput.Content;
		}

		private void SaveFile(FileGeneratorResult result)
		{
			using (FileStream stream = File.Create(result.FullName))
			{
				if (result.Content != null)
				{
					stream.Write(result.Content, 0, result.Content.Length);
				}
			}
		}

		private void SetItemProperties(FileGeneratorResult result, ProjectItem child)
		{
			child.Name = result.Name;
			//child.Properties.Item("IsCustomToolOutput").Value = true;

			if (result.BuildAction != FileGeneratorResult.BuildActionType.None)
			{
				child.Properties.Item("BuildAction").Value = (int)result.BuildAction;
			}

			if (!String.IsNullOrEmpty(result.CustomTool))
			{
				child.Properties.Item("CustomTool").Value = result.CustomTool;
			}
		}

		#endregion BaseCodeGenerator Methods
	}
}