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
using System.Runtime.InteropServices;
using System.Text;

using JsonFx.Jbst;
using JsonFx.Serialization;

namespace JsonFx.UI.Designer
{
	[ComVisible(true)]
	[Guid("A4863538-A079-4713-9CE1-3563B751F565")]
	public class JbstGenerator : MultipleFileGenerator
	{
		#region Generator Methods

		protected override IEnumerable<FileGeneratorResult> GenerateFiles(string inputFileName, string defaultNamespace, string inputFileContent)
		{
			try
			{
				string virtualPath = this.GetVirtualPath(inputFileName);

				var provider = this.GetCodeProvider();

				string clientOutput, serverOutput, serverExt = provider.FileExtension;
				new JbstCompiler(defaultNamespace, provider).Compile(virtualPath, inputFileContent, out clientOutput, out serverOutput);

				return new[]
				{
					// emit client-side control
					new FileGeneratorResult(inputFileName)
					{
						BuildAction = FileGeneratorResult.BuildActionType.EmbeddedResource,
						Extension = ".jbst.js",
						Content = Encoding.UTF8.GetBytes(clientOutput)
					},

					// emit server-side control
					new FileGeneratorResult(inputFileName)
					{
						BuildAction = FileGeneratorResult.BuildActionType.Compile,
						Extension = ".jbst."+serverExt,
						Content = Encoding.UTF8.GetBytes(serverOutput)
					}
				};
			}
			catch (DeserializationException ex)
			{
				this.AddError(1, ex.Message, (uint)ex.Line, (uint)ex.Column);
				return null;
			}
			catch (Exception ex)
			{
				this.AddError(1, ex.Message, 0, 0);
				return null;
			}
		}

		#endregion Generator Methods
	}
}