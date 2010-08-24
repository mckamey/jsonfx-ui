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
using System.IO;

namespace JsonFx.UI.Designer
{
	/// <summary>
	/// Represents a file generator build result
	/// </summary>
	public sealed class FileGeneratorResult
	{
		public enum BuildActionType : int
		{
			None = 0,
			Compile = 1,
			Content = 2,
			EmbeddedResource = 3
		}

		#region Fields

		private readonly string BasePath;
		private readonly string BaseName;

		#endregion Fields

		#region Init

		public FileGeneratorResult(string inputPath)
		{
			this.BasePath = Path.GetDirectoryName(inputPath)+Path.DirectorySeparatorChar;
			this.BaseName = Path.GetFileNameWithoutExtension(inputPath);
		}

		#endregion Init

		#region Properties

		/// <summary>
		/// The file extension of the generated file
		/// </summary>
		public string Extension { get; set; }

		/// <summary>
		/// The file content of the generated file
		/// </summary>
		public byte[] Content { get; set; }

		/// <summary>
		/// Allows specifying a custom tool
		/// </summary>
		public string CustomTool { get; set; }

		/// <summary>
		/// Allows specifying a custom build action
		/// </summary>
		public BuildActionType BuildAction { get; set; }

		/// <summary>
		/// Gets the resulting filename
		/// </summary>
		public string Name
		{
			get { return String.Concat(this.BaseName, this.Extension); }
		}

		/// <summary>
		/// Gets the resulting path and filename
		/// </summary>
		public string FullName
		{
			get { return String.Concat(this.BasePath, this.BaseName, this.Extension); }
		}

		internal bool Saved { get; set; }

		#endregion Properties
	}
}