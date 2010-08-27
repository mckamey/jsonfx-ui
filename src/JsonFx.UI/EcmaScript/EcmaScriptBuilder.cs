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
		/// Translates script block to a CodeDom method
		/// </summary>
		/// <param name="methodName"></param>
		/// <param name="script"></param>
		/// <returns></returns>
		public CodeMemberMethod Translate<TResult>(string methodName, string script)
		{
			CodeMemberMethod method = new CodeMemberMethod();
			method.Name = methodName;
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
#if DEBUG
			catch (Exception ex)
			{
				Trace.WriteLine(ex.Message);
#else
			catch
			{
#endif
				return null;
			}

			foreach (var node in block.Children)
			{
				CodeObject code = this.ProcessNode<TResult>(node);
				if (code is CodeExpression)
				{
					method.Statements.Add((CodeExpression)code);
				}
				else if (code is CodeStatement)
				{
					method.Statements.Add((CodeStatement)code);
				}
				else
				{
					// not yet supported
					return null;
				}
			}

			var index = method.Statements.Count-1;
			if (index >= 0)
			{
				// extract expression
				CodeExpression expr;
				if (method.Statements[index] is CodeExpressionStatement)
				{
					CodeExpressionStatement statement = (CodeExpressionStatement)method.Statements[index];
					expr = statement.Expression;
				}
				else if (method.Statements[index] is CodeMethodReturnStatement)
				{
					CodeMethodReturnStatement statement = (CodeMethodReturnStatement)method.Statements[index];
					expr = statement.Expression;
				}
				else
				{
					// bail.
					return method;
				}

				if (typeof(TResult) != typeof(object))
				{
					// wrap expression in a coercion call before return
					expr = new CodeMethodInvokeExpression(
						new CodeMethodReferenceExpression(
							new CodeThisReferenceExpression(),
							"CoerceType",
							new CodeTypeReference(typeof(TResult))),
						expr);
				}

				// convert expression statement to return statement
				method.Statements[index] = new CodeMethodReturnStatement(expr);
				method.ReturnType = new CodeTypeReference(typeof(TResult));
			}

			return method;
		}

		private CodeObject ProcessNode<TResult>(AstNode node)
		{
			ConstantWrapper constantWrapper = node as ConstantWrapper;
			if (constantWrapper != null)
			{
				return new CodePrimitiveExpression(constantWrapper.Value);
			}

			Member memberNode = node as Member;
			if (memberNode != null)
			{
				return this.ProcessMember<TResult>(memberNode);
			}

			ReturnNode returnNode = node as ReturnNode;
			if (returnNode != null)
			{
				CodeObject code = this.ProcessNode<TResult>(returnNode.Operand);
				if (code is CodeExpression)
				{
					return new CodeMethodReturnStatement((CodeExpression)code);
				}

				return null;
			}

			// TODO: process other types

			// not yet supported
			return null;
		}

		private CodeExpression ProcessMember<TResult>(Member memberNode)
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

			CodeObject root = this.ProcessNode<TResult>(memberNode.Root);
			if (root is CodeExpression)
			{
				return new CodeMethodInvokeExpression(
					new CodeThisReferenceExpression(),
					"GetProperty",
					(CodeExpression)root,
					new CodePrimitiveExpression(memberNode.Name));
			}

			return null;
		}

		private void OnCompilerError(object sender, JScriptExceptionEventArgs e)
		{
			//System.Diagnostics.Debugger.Break();
		}

		#endregion Methods
	}
}
