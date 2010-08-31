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
using System.Collections.Generic;
using System.Diagnostics;

using JsonFx.Serialization;
using Microsoft.Ajax.Utilities;

namespace JsonFx.EcmaScript
{
	internal class EcmaScriptBuilder
	{
		#region Constants

		private static readonly Type EmptyType = typeof(object);

		#endregion Constants

		#region Fields

		private readonly TypeCoercionUtility Coercion;
		private readonly CodeSettings CodeSettings;
		private readonly string[] GlobalVars;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		public EcmaScriptBuilder(DataWriterSettings settings)
		{
			this.Coercion = new TypeCoercionUtility(settings, true);

			// settings to balance compatibility with compactness
			this.CodeSettings = new CodeSettings
			{
				CollapseToLiteral = true,
				MacSafariQuirks = true,
				MinifyCode = true,
				OutputMode = OutputMode.SingleLine,
				RemoveUnneededCode = true,
				StripDebugStatements = false
			};

			this.GlobalVars = new[] { "JSON", "JsonML", "JsonFx" };
		}

		#endregion Init

		#region Methods

		/// <summary>
		/// Translates script block to a CodeDom method
		/// </summary>
		/// <param name="methodName"></param>
		/// <param name="script"></param>
		/// <returns></returns>
		public void Translate(TranslationResult result)
		{
			if (String.IsNullOrEmpty(result.Script))
			{
				// technically undefined
				return;
			}

			JSParser parser = new JSParser(result.Script, this.GlobalVars);
			parser.CompilerError += new EventHandler<JScriptExceptionEventArgs>(this.OnCompilerError);
			Block block;
			try
			{
				block = parser.Parse(this.CodeSettings);
				result.Script = block.ToCode(ToCodeFormat.Normal);
			}
#if DEBUG
			catch (Exception ex)
			{
				Trace.WriteLine(ex.Message);
#else
			catch
			{
#endif
				result.IsClientOnly = true;
				return;
			}

			if (block.Count < 1)
			{
				// empty result
				return;
			}

			bool needsReturn;
			if (block.Count == 1 && block[0] is FunctionObject)
			{
				block = ((FunctionObject)block[0]).Body;
				needsReturn = false;
			}
			else
			{
				needsReturn = true;
			}

			var children = this.VisitBlock(block, result.ResultType);
			if (children == null)
			{
				result.IsClientOnly = true;
				return;
			}

			foreach (var child in children)
			{
				if (!result.AddResult(child))
				{
					// not yet supported
					result.IsClientOnly = true;
					return;
				}
			}

			result.EnsureReturnType(needsReturn);
		}

		private ExpressionResult Visit(AstNode node, Type expectedType)
		{
			ConstantWrapper constantWrapper = node as ConstantWrapper;
			if (constantWrapper != null)
			{
				return this.VisitConstantWrapper(constantWrapper, expectedType);
			}

			Member memberNode = node as Member;
			if (memberNode != null)
			{
				return this.VisitMember(memberNode, expectedType);
			}

			CallNode callNode = node as CallNode;
			if (callNode != null)
			{
				return null;
				//return this.VisitCallNode(callNode, expectedType);
			}

			ReturnNode returnNode = node as ReturnNode;
			if (returnNode != null)
			{
				ExpressionResult code = this.Visit(returnNode.Operand, expectedType);
				if (code == null || code.Expression == null)
				{
					return code;
				}

				return new ExpressionResult
				{
					Statement = new CodeMethodReturnStatement((CodeExpression)code.Expression)
				};
			}

			if (node is ThisLiteral)
			{
				return new ExpressionResult
				{
					Expression = new CodeThisReferenceExpression(),
					ExpressionType = typeof(object) // TODO: get real JBST type
				};
			}

			BinaryOperator binaryOp = node as BinaryOperator;
			if (binaryOp != null)
			{
				return this.VisitBinaryOperator(expectedType, binaryOp);
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

		private IEnumerable<ExpressionResult> VisitBlock(Block block, Type expectedType)
		{
			List<ExpressionResult> results = new List<ExpressionResult>(block.Count);
			foreach (var node in block.Children)
			{
				ExpressionResult expr = this.Visit(node, expectedType);
				results.Add(expr);
			}

			return results;
		}

		private ExpressionResult VisitConstantWrapper(ConstantWrapper constantWrapper, Type expectedType)
		{
			CodeExpression expr;
			Type exprType = expectedType;

			object value = constantWrapper.Value;
			if (value != null)
			{
				Type actualType = value.GetType();
				expr = this.EnsureCoercion(expectedType, actualType, new CodePrimitiveExpression(value));
				if (expr is CodePrimitiveExpression)
				{
					exprType = actualType;
				}
			}
			else if (expectedType == EmptyType || expectedType.IsClass)
			{
				expr = new CodePrimitiveExpression(null);
			}
			else
			{
				expr = new CodeDefaultValueExpression(new CodeTypeReference(expectedType));
			}

			return new ExpressionResult
			{
				Expression = expr,
				ExpressionType = exprType
			};
		}

		private ExpressionResult VisitMember(Member memberNode, Type expectedType)
		{
			if (memberNode.Root is ThisLiteral)
			{
				// TODO: pass parameter list as property on TranslationResult
				// these are remapped to method args
				switch (memberNode.Name)
				{
					case "data":
					{
						return this.VisitArgumentReference(expectedType, typeof(object), memberNode.Name);
					}
					case "index":
					case "count":
					{
						return this.VisitArgumentReference(expectedType, typeof(int), memberNode.Name);
					}
					default:
					{
						// not yet supported
						return null;
					}
				}
			}

			ExpressionResult root = this.Visit(memberNode.Root, expectedType);
			if (root == null)
			{
				return null;
			}

			return new ExpressionResult
			{
				Expression = new CodeMethodInvokeExpression(
					new CodeThisReferenceExpression(),
					"GetProperty",
					root.Expression,
					new CodePrimitiveExpression(memberNode.Name)),
				ExpressionType = typeof(object)
			};
		}

		private ExpressionResult VisitArgumentReference(Type expectedType, Type actualType, string name)
		{
			CodeExpression expr = this.EnsureCoercion(expectedType, actualType, new CodeArgumentReferenceExpression(name));

			return new ExpressionResult
			{
				Expression =  expr,
				ExpressionType = (expr is CodeArgumentReferenceExpression) ? actualType : expectedType
			};
		}

		private ExpressionResult VisitBinaryOperator(Type expectedType, BinaryOperator binaryOp)
		{
			var left = this.Visit(binaryOp.Operand1, expectedType);
			var right = this.Visit(binaryOp.Operand2, expectedType);

			if (left == null || left.Expression == null ||
				right == null || right.Expression == null)
			{
				return null;
			}

			CodeBinaryOperatorType op = EcmaScriptBuilder.MapBinaryOperator(binaryOp.OperatorToken);

			Type exprType = this.EnsureCompatibleTypes(left, right, expectedType);

			return new ExpressionResult
			{
				ExpressionType = exprType,
				Expression = new CodeBinaryOperatorExpression(left.Expression, op, right.Expression)
			};
		}

		private Type EnsureCompatibleTypes(ExpressionResult left, ExpressionResult right, Type expectedType)
		{
			if (left.ExpressionType == EmptyType)
			{
				if (right.ExpressionType == EmptyType)
				{
					// convert both objects to expected type
					left.Expression = this.DeferredCoerceType(expectedType, left.Expression);
					right.Expression = this.DeferredCoerceType(expectedType, right.Expression);
					return expectedType;
				}

				// convert one object to the other type
				left.Expression = this.DeferredCoerceType(right.ExpressionType, left.Expression);
				return (left.ExpressionType = right.ExpressionType);
			}

			if (right.ExpressionType == EmptyType)
			{
				// convert one object to the other type
				right.Expression = this.DeferredCoerceType(left.ExpressionType, right.Expression);
				return (right.ExpressionType = left.ExpressionType);
			}

			if (left.ExpressionType.IsAssignableFrom(right.ExpressionType))
			{
				// right compatible with left type
				return left.ExpressionType;
			}

			if (right.ExpressionType.IsAssignableFrom(left.ExpressionType))
			{
				// left is compatible with right type
				return right.ExpressionType;
			}

			// arbitrary choice needed, use left type
			right.Expression = this.DeferredCoerceType(left.ExpressionType, right.Expression);
			return (right.ExpressionType = left.ExpressionType);
		}

		private ExpressionResult VisitCallNode(CallNode callNode, Type expectedType)
		{
			int i = 0;
			CodeExpression[] args = new CodeExpression[callNode.Arguments.Count];
			foreach (var arg in callNode.Arguments.Children)
			{
				ExpressionResult code = this.Visit(arg, expectedType);
				if (code == null ||
					code.Expression == null)
				{
					return null;
				}

				args[i++] = code.Expression;
			}

			Member memberNode = callNode.Function as Member;
			if (memberNode != null)
			{
				ExpressionResult code = this.Visit(memberNode.Root, expectedType);
				if (code != null &&
					code.Expression != null)
				{
					CodeExpression expr = new CodeMethodInvokeExpression(code.Expression, memberNode.Name, args);

					return new ExpressionResult
					{
						Expression = this.DeferredCoerceType(expectedType, expr),
						ExpressionType = expectedType
					};
				}
			}

			return null;
		}

		private CodeExpression EnsureCoercion(Type expectedType, Type exprType, CodeExpression expr)
		{
			if (expectedType == EmptyType ||
				expectedType.IsAssignableFrom(exprType))
			{
				return expr;
			}

			return this.DeferredCoerceType(expectedType, expr);
		}

		private CodeExpression DeferredCoerceType(Type targetType, CodeExpression expr)
		{
			if (expr is CodePrimitiveExpression)
			{
				// coerce at compile time so doesn't need to happen at runtime
				object value = ((CodePrimitiveExpression)expr).Value;
				((CodePrimitiveExpression)expr).Value = this.Coercion.CoerceType(targetType, value);

				return expr;
			}

			// wrap expression in a coercion call before return
			return new CodeMethodInvokeExpression(
				new CodeMethodReferenceExpression(
					new CodeThisReferenceExpression(),
					"CoerceType",
					new CodeTypeReference(targetType)),
				expr);
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
			JScriptException exception = e.Exception;

			// TODO: report JavaScript error
		}

		#endregion Methods
	}
}
