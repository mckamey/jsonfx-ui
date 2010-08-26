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
using System.CodeDom;
using System.Diagnostics;

using JsonFx.Model;
using JsonFx.Serialization;
using Microsoft.Ajax.Utilities;

using TokenSequence=System.Collections.Generic.IEnumerable<JsonFx.Serialization.Token<JsonFx.Model.ModelTokenType>>;

namespace JsonFx.EcmaScript
{
	internal class EcmaScriptBuilder
	{
		#region Fields

		private readonly CodeSettings CodeSettings;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="hostType"></param>
		public EcmaScriptBuilder()
		{
			this.CodeSettings = new CodeSettings
			{
				// TODO.
			};
		}

		#endregion Init

		#region Methods

		/// <summary>
		/// Converts
		/// </summary>
		/// <param name="script"></param>
		/// <param name="methodName"></param>
		/// <returns></returns>
		public CodeMemberMethod Translate(string script, string methodName, Type returnType)
		{
			CodeMemberMethod method = new CodeMemberMethod();
			method.Name = methodName;
			method.ReturnType = new CodeTypeReference(returnType);
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TokenSequence), "data"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
			method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));

			if (String.IsNullOrEmpty(script))
			{
				// technically undefined
				return method;
			}

			JSParser parser = new JSParser(script, null);
			parser.CompilerError += new EventHandler<JScriptExceptionEventArgs>(this.OnCompilerError);
			Block block;
			try
			{
				block = parser.Parse(this.CodeSettings);
			}
			catch (Exception ex)
			{
#if DEBUG
				Trace.WriteLine(ex.Message);
#endif
				return null;
			}

			if (block.Count < 1)
			{
				// technically undefined
				return method;
			}

			if (block.Count > 1)
			{
				// not yet supported
				return method;
			}

			Member memberNode = block[0] as Member;
			if (memberNode == null)
			{
				// not yet supported
				return null;
			}

			CodeExpression expr = this.ProcessMember(memberNode);
			method.Statements.Add(new CodeMethodReturnStatement(expr));

			return method;
		}

		private CodeExpression ProcessMember(Member memberNode)
		{
			if (memberNode.Root is ThisLiteral)
			{
				switch (memberNode.Name)
				{
					case "data":
					case "index":
					case "count":
					{
						return new CodeArgumentReferenceExpression(memberNode.Name);
					}
					default:
					{
						// not yet supported
						return null;
					}
				}
			}
			else if (memberNode.Root is Member)
			{
				CodeExpression root = this.ProcessMember((Member)memberNode.Root);
				if (root == null)
				{
					return null;
				}

				return new CodeMethodInvokeExpression(
					new CodeTypeReferenceExpression(typeof(ModelSubsequencer)),
					"Property",
					root,
					new CodeObjectCreateExpression(typeof(DataName), new CodePrimitiveExpression(memberNode.Name)));
			}

			// not yet supported
			return null;
		}

		private void OnCompilerError(object sender, JScriptExceptionEventArgs e)
		{
			//System.Diagnostics.Debugger.Break();
		}

		#endregion Methods
	}
}
