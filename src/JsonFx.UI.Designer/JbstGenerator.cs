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
using System.Runtime.InteropServices;
using System.Text;

using JsonFx.Jbst;
using JsonFx.Serialization;
using Microsoft.VisualStudio.Shell;

namespace JsonFx.UI.Designer
{
	[ComVisible(true)]
	[Guid("A4863538-A079-4713-9CE1-3563B751F565")]
	//[CodeGeneratorRegistration(typeof(JbstGenerator), "JBST Code Generator (C#)", "{fae04ec1-301f-11d3-bf4b-00c04f79efbc}", GeneratesDesignTimeSource=true)]
	//[CodeGeneratorRegistration(typeof(JbstGenerator), "JBST Code Generator (VB)", "{164b10b9-b200-11d0-8c61-00a0c91e29d5}", GeneratesDesignTimeSource=true)]
	//[CodeGeneratorRegistration(typeof(JbstGenerator), "JBST Code Generator (F#)", "{F2A71F9B-5D33-465A-A702-920D77279786}", GeneratesDesignTimeSource=true)]
	//[CodeGeneratorRegistration(typeof(JbstGenerator), "JBST Code Generator (J#)", "{E6FDF8B0-F3D1-11D4-8576-0002A516ECE8}", GeneratesDesignTimeSource=true)]
	//[ProvideObject(typeof(JbstGenerator))]
	public class JbstGenerator : BaseCodeGenerator
	{
		#region BaseCodeGeneratorWithSite Methods

		protected override string GetDefaultExtension()
		{
			return ".jbst.js";
		}

		protected override byte[] GenerateCode(string inputFileName, string defaultNamespace, string inputFileContent)
		{
			try
			{
				string outputContent = new JbstCompiler(defaultNamespace).Compile(inputFileName, inputFileContent);

				return Encoding.UTF8.GetBytes(outputContent);
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

		#endregion BaseCodeGeneratorWithSite Methods
	}
}