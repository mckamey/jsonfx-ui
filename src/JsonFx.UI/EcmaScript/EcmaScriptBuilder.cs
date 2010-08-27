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

				bool needsCoercion = (typeof(TResult) != typeof(object));
				if (needsCoercion)
				{
					if (expr is CodeArgumentReferenceExpression)
					{
						string paramName = ((CodeArgumentReferenceExpression)expr).ParameterName;
						if ((paramName == "index") || (paramName == "count"))
						{
							if (typeof(TResult) == typeof(int))
							{
								needsCoercion = false;
							}
						}
					}
					else if (expr is CodePrimitiveExpression)
					{
						if (((CodePrimitiveExpression)expr).Value is TResult)
						{
							needsCoercion = false;
						}
					}

					if (needsCoercion)
					{
						expr = this.WrapWithCoercion<TResult>(expr);
					}
				}

				// convert expression statement to return statement
				method.Statements[index] = new CodeMethodReturnStatement(expr);
				method.ReturnType = new CodeTypeReference(typeof(TResult));
			}

			return method;
		}

		private CodeExpression WrapWithCoercion<TResult>(CodeExpression expr)
		{
			// wrap expression in a coercion call before return
			return new CodeMethodInvokeExpression(
				new CodeMethodReferenceExpression(
					new CodeThisReferenceExpression(),
					"CoerceType",
					new CodeTypeReference(typeof(TResult))),
				expr);
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

			CallNode callNode = node as CallNode;
			if (callNode != null)
			{
				return null;
				//return this.ProcessCallNode<TResult>(callNode);
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

			if (node is ThisLiteral)
			{
				return new CodeThisReferenceExpression();
			}

			BinaryOperator binaryOp = node as BinaryOperator;
			if (binaryOp != null)
			{
				var left = this.ProcessNode<TResult>(binaryOp.Operand1) as CodeExpression;
				var right = this.ProcessNode<TResult>(binaryOp.Operand2) as CodeExpression;
				if (left == null || right == null)
				{
					return null;
				}

				CodeBinaryOperatorType op = EcmaScriptBuilder.MapBinaryOperator(binaryOp.OperatorToken);

				return new CodeBinaryOperatorExpression(left, op, right);
			}

			Lookup lookupNode = node as Lookup;
			if (lookupNode != null)
			{
				// TODO: namespace or var
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

		private CodeExpression ProcessCallNode<TResult>(CallNode callNode)
		{
			int i = 0;
			CodeExpression[] args = new CodeExpression[callNode.Arguments.Count];
			foreach (var arg in callNode.Arguments.Children)
			{
				CodeObject code = this.ProcessNode<TResult>(arg);
				if (code is CodeExpression)
				{
					args[i++] = (CodeExpression)code;
				}
				else
				{
					return null;
				}
			}

			Member memberNode = callNode.Function as Member;
			if (memberNode != null)
			{
				CodeObject code = this.ProcessNode<TResult>(memberNode.Root);
				if (code is CodeExpression)
				{
					return new CodeMethodInvokeExpression(
						(CodeExpression)code,
						memberNode.Name,
						args);
				}
			}

			return null;
		}

		private static CodeBinaryOperatorType MapBinaryOperator(JSToken binaryOp)
		{
			switch (binaryOp)
			{
				case JSToken.Plus:
				{
					return CodeBinaryOperatorType.Add;
				}
				case JSToken.Minus:
				{
					return CodeBinaryOperatorType.Subtract;
				}
				case JSToken.Multiply:
				{
					return CodeBinaryOperatorType.Multiply;
				}
				case JSToken.Divide:
				{
					return CodeBinaryOperatorType.Divide;
				}
				case JSToken.Modulo:
				{
					return CodeBinaryOperatorType.Modulus;
				}
				case JSToken.Assign:
				{
					return CodeBinaryOperatorType.Assign;
				}
				case JSToken.NotEqual:
				{
					return CodeBinaryOperatorType.IdentityInequality;
				}
				case JSToken.Equal:
				{
					//return CodeBinaryOperatorType.IdentityEquality;
					return CodeBinaryOperatorType.ValueEquality;
				}
				case JSToken.BitwiseOr:
				{
					return CodeBinaryOperatorType.BitwiseOr;
				}
				case JSToken.BitwiseAnd:
				{
					return CodeBinaryOperatorType.BitwiseAnd;
				}
				case JSToken.LogicalOr:
				{
					return CodeBinaryOperatorType.BooleanOr;
				}
				case JSToken.LogicalAnd:
				{
					return CodeBinaryOperatorType.BooleanAnd;
				}
				case JSToken.LessThan:
				{
					return CodeBinaryOperatorType.LessThan;
				}
				case JSToken.LessThanEqual:
				{
					return CodeBinaryOperatorType.LessThanOrEqual;
				}
				case JSToken.GreaterThan:
				{
					return CodeBinaryOperatorType.GreaterThan;
				}
				case JSToken.GreaterThanEqual:
				{
					return CodeBinaryOperatorType.GreaterThanOrEqual;
				}
				default:
				{
					return (CodeBinaryOperatorType)(-1);
				}
			}
		}

		private void OnCompilerError(object sender, JScriptExceptionEventArgs e)
		{
			//System.Diagnostics.Debugger.Break();
		}

		#endregion Methods
	}
}
