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
using System.Collections;
using System.Collections.Generic;
using System.IO;

using JsonFx.EcmaScript;
using JsonFx.Html;
using JsonFx.Markup;
using JsonFx.Model;
using JsonFx.Serialization;
using JsonFx.Utils;

namespace JsonFx.Jbst
{
	/// <summary>
	/// Builds the server-side implementation of a given JBST
	/// </summary>
	internal class JbstViewBuilder
	{
		#region Constants

		private const string TemplateMethodFormat = "T_{0:x4}";
		private const string CodeBlockMethodFormat = "B_{0:x4}";
		private const string FieldFormat = "{0}_{1:x}";
		private const string LocalVarFormat = "{0}_{1:x}";

		private static readonly DataName JbstVisible = new DataName("visible", "jbst", null);
		//private const string JbstOnInit = "oninit";
		//private const string JbstOnLoad = "onload";

		private static readonly char[] NameDelim = new[] { ':' };

		private static readonly object Key_VarCount = new object();
		private static readonly object Key_EmptyBody = new object();

		#endregion Constants

		#region Fields

		private readonly ModelAnalyzer Analyzer;
		private readonly HtmlFormatter HtmlFormatter;
		private readonly EcmaScriptFormatter JSFormatter;
		private readonly EcmaScriptBuilder JSBuilder;
		private CodeTypeDeclaration viewType = null;
		private int counter;

		#endregion Fields

		#region Init

		/// <summary>
		/// Ctor
		/// </summary>
		/// <param name="settings"></param>
		public JbstViewBuilder(DataWriterSettings settings)
		{
			this.JSBuilder = new EcmaScriptBuilder(settings);

			this.HtmlFormatter = new HtmlFormatter(settings)
			{
				CanonicalForm = false,
				EncodeNonAscii = true,
				EmptyAttributes = HtmlFormatter.EmptyAttributeType.Html
			};

			this.JSFormatter = new EcmaScriptFormatter(settings)
			{
				EncodeLessThan = true
			};

			this.Analyzer = new ModelAnalyzer(new DataReaderSettings(settings.Resolver, settings.Filters));
		}

		#endregion Init

		#region Build Method

		public CodeCompileUnit Build(CompilationState state)
		{
			if (state == null)
			{
				throw new ArgumentNullException("state");
			}

			this.counter = 0;
			this.HtmlFormatter.ResetScopeChain();

			CodeCompileUnit code = new CodeCompileUnit();

			#region namespace JbstNamespace

			state.EnsureName();

			string typeNS, typeName;
			this.SplitTypeName(state.JbstName, out typeNS, out typeName);

			CodeNamespace ns = new CodeNamespace(typeNS);
			code.Namespaces.Add(ns);

			#endregion namespace JbstNamespace

			#region public partial class JbstTypeName

			CodeTypeDeclaration viewType = new CodeTypeDeclaration();
			viewType.IsClass = true;
			viewType.Name = typeName;
			viewType.IsPartial = true;
			viewType.Attributes = MemberAttributes.Public|MemberAttributes.Final;

			viewType.BaseTypes.Add(typeof(JbstView));
			ns.Types.Add(viewType);

			#endregion public sealed class JbstTypeName

			#region [BuildPath(virtualPath)]

			string virtualPath = PathUtility.EnsureAppRelative(state.FilePath);

			CodeAttributeDeclaration attribute = new CodeAttributeDeclaration(
				new CodeTypeReference(typeof(BuildPathAttribute)),
				new CodeAttributeArgument(new CodePrimitiveExpression(virtualPath)));
			viewType.CustomAttributes.Add(attribute);

			#endregion [BuildPath(virtualPath)]

			#region Init

			CodeConstructor ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			viewType.Members.Add(ctor);

			ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(JbstView), "view"));
			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("view"));
			viewType.Members.Add(ctor);

			ctor = new CodeConstructor();
			ctor.Attributes = MemberAttributes.Public;
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(DataWriterSettings), "settings"));
			ctor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(IClientIDStrategy), "clientID"));
			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("settings"));
			ctor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("clientID"));
			viewType.Members.Add(ctor);

			CodeMemberMethod init = new CodeMemberMethod();
			init.Name = "Init";
			init.Attributes = MemberAttributes.Family|MemberAttributes.Override;
			viewType.Members.Add(init);

			#endregion Init

			TranslationState output = new TranslationState(this.HtmlFormatter);

			this.viewType = viewType;
			try
			{
				// build control tree
				this.ProcessTemplate(state, output, true);
			}
			finally
			{
				this.viewType = null;
			}

			foreach (CodeTypeMember member in output.Code)
			{
				viewType.Members.Add(member);
			}

			return code;
		}

		#endregion Build Method

		#region Process Methods

		private string ProcessTemplate(CompilationState state, TranslationState output, bool isRoot)
		{
			var content = state.TransformContent();
			if (content == null)
			{
				return null;
			}

			// effectively FirstOrDefault
			foreach (var input in this.Analyzer.Analyze(content))
			{
				this.ProcessChild(input, output);
				output.Flush();

				#region private void methodName(TextWriter writer, object data, int index, int count)

				// each template gets built as a method as it can be called in a loop
				string methodName = isRoot ? "Root" : String.Format(TemplateMethodFormat, ++this.counter);

				CodeMemberMethod method = new CodeMemberMethod();

				method.Name = methodName;
				method.Attributes = isRoot ? MemberAttributes.Override|MemberAttributes.Family : MemberAttributes.Private;
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(TextWriter), "writer"));
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(object), "data"));
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "index"));
				method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(int), "count"));

				#endregion private void methodName(TextWriter writer, object data, int index, int count)

				// add all statements to the method
				for (int i=0, length=output.Code.Count; i<length; i++)
				{
					var code = output.Code[i];
					bool remove = false;

					if (code == null)
					{
						remove = true;
					}
					else if (code is CodeExpression)
					{
						method.Statements.Add((CodeExpression)code);
						remove = true;
					}
					else if (code is CodeStatement)
					{
						method.Statements.Add((CodeStatement)code);
						remove = true;
					}

					if (remove)
					{
						output.Code.RemoveAt(i);
						i--;
						length--;
					}
				}

				output.Code.Add(method);

				return methodName;
			}

			return null;
		}

		private void ProcessChild(object child, TranslationState output)
		{
			if (child is IList)
			{
				// JsonML element
				this.ProcessElement((IList)child, output);
			}

			else if (child is JbstCommand)
			{
				// flush the buffer
				output.Flush();

				IList<CodeObject> code = this.ProcessCommand((JbstCommand)child, false);
				if (code != null)
				{
					for (int j=0, lines=code.Count; j<lines; j++)
					{
						var line = code[j];
						if (line == null)
						{
							code.RemoveAt(j);
							j--;
							lines--;
						}

						if (line is CodeExpression)
						{
							code[j] = this.EmitExpression((CodeExpression)line);
						}
					}
					output.Code.AddRange(code);
				}
			}

			else
			{
				// process as literal
				output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, child));
			}
		}

		private void ProcessElement(IList input, TranslationState output)
		{
			if (input == null || input.Count < 1)
			{
				return;
			}

			int tempStart = output.Buffer.Count;

			DataName tagName = this.SplitDataName((string)input[0], false);
			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, tagName));

			object tagID = null;
			CodeConditionStatement visible = null;
			IDictionary<DataName, object> attrs = null;

			TranslationState temp = output;
			int i = 1,
				count = input.Count;

			if (count > 1 && input[1] is IDictionary<string, object>)
			{
				IDictionary<string, object> allAttr = (IDictionary<string, object>)input[i];
				attrs = this.ProcessAttributes(allAttr, temp.Buffer);
				i++;

				if (attrs != null)
				{
					if (attrs.ContainsKey(JbstViewBuilder.JbstVisible))
					{
						var visibleCode = this.ProcessCommand(attrs[JbstViewBuilder.JbstVisible] as JbstCommand, true);
						if (visibleCode.Count > 0)
						{
							int lines = visibleCode.Count-1;

							CodeExpression expr = (lines >= 0) ?
								visibleCode[lines] as CodeExpression :
								null;

							if (expr != null)
							{
								for (int j=0; j<lines; j++)
								{
									output.Code.Add(visibleCode[j]);
								}

								// flush any buffer before elem
								output.Flush(tempStart);

								visible = new CodeConditionStatement();
								if (expr is CodeBinaryOperatorExpression)
								{
									visible.Condition = expr;
								}
								else
								{
									visible.Condition = this.JSBuilder.DeferredCoerceType(typeof(bool), expr);
								}
								output.Code.Add(visible);

								// capture element output for conditional block
								temp = new TranslationState(output);
							}
						}

						attrs.Remove(JbstViewBuilder.JbstVisible);
					}

					if (attrs.Count > 0)
					{
						if (allAttr.ContainsKey("id"))
						{
							tagID = allAttr["id"] as String;
							allAttr.Remove("id");
						}

						if (tagID == null)
						{
							string varID;
							temp.Code.Add(this.GenerateClientIDVar(out varID));
							tagID = this.EmitVarValue(varID);

							// emit markup with replacement value
							string replacement = Guid.NewGuid().ToString("B");
							temp.Replacements.Add(new KeyValuePair<string, CodeObject>(replacement, (CodeObject)tagID));

							temp.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("id")));
							temp.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, replacement));
						}
					}

					if (attrs.Count < 1)
					{
						attrs = null;
					}
				}
			}

			for (; i<count; i++)
			{
				var item = input[i];

				this.ProcessChild(item, temp);
			}

			temp.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));

			if (attrs != null)
			{
				// add script to temp block so if not-visible won't emit
				new PatchClientBlock(
					tagID,
					this.EmitExprAsJson(new CodeArgumentReferenceExpression("data")),
					this.EmitExprAsJson(new CodeArgumentReferenceExpression("index")),
					this.EmitExprAsJson(new CodeArgumentReferenceExpression("count")),
					attrs).Format(temp.Buffer, temp.Replacements);
			}

			if (visible != null)
			{
				// flush the buffer
				temp.Flush();

				// add all statements to the conditional
				foreach (var code in temp.Code)
				{
					if (code == null)
					{
						continue;
					}

					if (code is CodeStatement)
					{
						visible.TrueStatements.Add((CodeStatement)code);
					}
					else if (code is CodeExpression)
					{
						visible.TrueStatements.Add((CodeExpression)code);
					}
					else
					{
						// all others get added to parent
						output.Code.Add(code);
					}
				}
			}
		}

		private IDictionary<DataName, object> ProcessAttributes(IDictionary<string, object> attributes, List<Token<MarkupTokenType>> buffer)
		{
			IDictionary<DataName, object> attrs = null;

			foreach (var attr in attributes)
			{
				DataName name = this.SplitDataName(attr.Key, true);

				object value = attr.Value;
				bool isCommand = value is JbstCommand;

				if (!isCommand && name.Prefix == "jbst")
				{
					value = new JbstExpressionBlock(value as string);
					isCommand = true;
				}

				if (isCommand)
				{
					if (attrs == null)
					{
						attrs = new Dictionary<DataName, object>(attributes.Count);
					}

					attrs[name] = value;
				}
				else
				{
					buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, name));
					buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, value));
				}
			}

			return attrs;
		}

		private IList<CodeObject> ProcessCommand(JbstCommand command, bool isAttribute)
		{
			switch (command.CommandType)
			{
				case JbstCommandType.ExpressionBlock:
				case JbstCommandType.UnparsedBlock:
				case JbstCommandType.StatementBlock:
				{
					return this.Translate<object>(command);
				}
				case JbstCommandType.TemplateReference:
				{
					JbstTemplateReference reference = (JbstTemplateReference)command;
					return this.BuildBindReferenceCall(reference.NameExpr, reference.DataExpr, reference.IndexExpr, reference.CountExpr);
				}
				case JbstCommandType.InlineTemplate:
				{
					JbstInlineTemplate inline = (JbstInlineTemplate)command;

					TranslationState output = new TranslationState(this.HtmlFormatter);
					string childMethod = this.ProcessTemplate(inline.State, output, false);
					if (!String.IsNullOrEmpty(childMethod))
					{
						var call = this.BuildBindAdapterCall(childMethod, inline.DataExpr, inline.IndexExpr, inline.CountExpr, false);
						output.Code.Add(call);
					}
					output.Flush();
					return output.Code;
				}
				case JbstCommandType.Placeholder:
				{
					// TODO
					return null;
				}
				case JbstCommandType.CommentBlock:
				{
					JbstCommentBlock comment = (JbstCommentBlock)command;
					return new CodeObject[] { this.EmitExpression(new CodePrimitiveExpression(String.Concat("<!--", comment.Code, "-->"))) };
				}
				case JbstCommandType.DeclarationBlock:
				{
					// these have been compiled away
					return null;
				}
				default:
				{
					// TODO:
					return null;
				}
			}
		}

		#endregion Process Methods

		#region Build Methods

		private IList<CodeObject> BuildBindReferenceCall(object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			#region this.Bind(new ExternalTemplate(), writer, dataExpr, indexExpr, countExpr);

			CodeExpression nameCode;
			if (nameExpr is string)
			{
				string name = (string)nameExpr;
				if (EcmaScriptIdentifier.IsValidIdentifier(name, true))
				{
					nameCode = this.GenerateExternalTemplateField(name);
				}
				else
				{
					nameCode = this.TranslateExpression<object>(nameExpr);
				}
			}
			else
			{
				nameCode = this.TranslateExpression<object>(nameExpr);
			}

			CodeExpression dataCode, indexCode, countCode;
			this.ProcessArgs(dataExpr, indexExpr, countExpr, out dataCode, out indexCode, out countCode);

			if (nameCode == null || dataCode == null || indexCode == null || countCode == null)
			{
				// force into client mode
				return this.BuildClientReference(nameExpr, dataExpr, indexExpr, countExpr);
			}

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"Bind",
				nameCode,
				new CodeArgumentReferenceExpression("writer"),
				dataCode,
				indexCode,
				countCode);

			#endregion this.Bind(new ExternalTemplate(), writer, dataExpr, indexExpr, countExpr);

			return new CodeObject[] { new CodeExpressionStatement(methodCall) };
		}

		private CodeExpression GenerateExternalTemplateField(string externalType)
		{
			CodeMemberMethod init = null;
			CodeMemberField field;

			foreach (CodeTypeMember member in this.viewType.Members)
			{
				field = member as CodeMemberField;
				if (field == null)
				{
					CodeMemberMethod temp = member as CodeMemberMethod;
					if (temp != null &&
						temp.Name == "Init")
					{
						// save Init in case need to generate initialization
						init = temp;
					}
					continue;
				}

				if (StringComparer.Ordinal.Equals(field.Type.BaseType, externalType))
				{
					// reuse a previously created instance
					return new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), field.Name);
				}
			}

			if (init == null)
			{
				throw new InvalidOperationException("Constructor not found.");
			}

			#region private externalType t_XXXX;

			field = new CodeMemberField(externalType, String.Format(FieldFormat, "t", ++this.counter));
			field.Attributes = MemberAttributes.Private;
			this.viewType.Members.Add(field);

			#endregion private externalType t_XXXX;

			#region this.t_XXXX

			var fieldRef = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), field.Name);

			#endregion this.t_XXXX

			#region this.t_XXXX = new externalType(this);
			
			var fieldInit = new CodeAssignStatement(
				fieldRef,
				new CodeObjectCreateExpression(
					externalType,
					new CodeThisReferenceExpression()));

			init.Statements.Add(fieldInit);

			#endregion this.t_XXXX = new externalType(this);

			return fieldRef;
		}

		private void ProcessArgs(object dataExpr, object indexExpr, object countExpr,
			out CodeExpression dataCode, out CodeExpression indexCode, out CodeExpression countCode)
		{
			dataCode = this.TranslateExpression<object>(dataExpr);
			indexCode = (dataCode != null) ? this.TranslateExpression<int>(indexExpr) as CodeExpression : null;
			countCode = (indexCode != null) ? this.TranslateExpression<int>(countExpr) as CodeExpression : null;

			if (dataCode is CodeDefaultValueExpression)
			{
				// expression evaluated to an empty method so pass surrounding scope through
				dataCode = new CodeArgumentReferenceExpression("data");
			}
			if (indexCode is CodeDefaultValueExpression)
			{
				// expression evaluated to an empty method so pass surrounding scope through
				indexCode = new CodeArgumentReferenceExpression("index");
			}
			if (countCode is CodeDefaultValueExpression)
			{
				// expression evaluated to an empty method so pass surrounding scope through
				countCode = new CodeArgumentReferenceExpression("count");
			}
		}

		private IList<CodeObject> BuildClientReference(object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			TranslationState output = new TranslationState(this.HtmlFormatter);

			string varID;
			output.Code.Add(this.GenerateClientIDVar(out varID));

			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, new DataName("noscript")));
			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("id")));

			string replacement = Guid.NewGuid().ToString("B");
			CodeObject varVal = this.EmitVarValue(varID);
			output.Replacements.Add(new KeyValuePair<string, CodeObject>(replacement, varVal));

			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, replacement));
			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));

			// emit replacement client block
			new ReplaceClientBlock(varVal, nameExpr, dataExpr, indexExpr, countExpr).Format(output.Buffer, output.Replacements);

			output.Flush();

			return output.Code;
		}

		private IList<CodeObject> BuildWrapperReference(object nameExpr, object dataExpr, object indexExpr, object countExpr)
		{
			TranslationState output = new TranslationState(this.HtmlFormatter);

			string varID;
			output.Code.Add(this.GenerateClientIDVar(out varID));

			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementBegin, new DataName("div")));
			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Attribute, new DataName("id")));

			string replacement = Guid.NewGuid().ToString("B");
			CodeObject varVal = this.EmitVarValue(varID);
			output.Replacements.Add(new KeyValuePair<string, CodeObject>(replacement, varVal));

			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, replacement));
			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd));

			// TODO: inline content goes here
			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.Primitive, "[ inline content goes here ]"));

			output.Buffer.Add(new Token<MarkupTokenType>(MarkupTokenType.ElementEnd)); // div

			// emit replacement client block
			new ReplaceClientBlock(varVal, nameExpr, dataExpr, indexExpr, countExpr).Format(output.Buffer, output.Replacements);

			output.Flush();

			return output.Code;
		}

		public string FormatExpression(object argument)
		{
			if (argument == null)
			{
				return null;
			}

			if (argument is string)
			{
				// directly use as inline expression
				argument = new JbstExpressionBlock((string)argument);
			}

			JbstCommand command = argument as JbstCommand;
			if (command != null)
			{
				//return block.Code;

				string code;
				using (var writer = new StringWriter())
				{
					command.Format(null, writer);
					code = writer.GetStringBuilder().ToString();
				}
			}

			// convert to token sequence and allow formatter to emit as JS primitive
			return this.JSFormatter.Format(new[] { new Token<ModelTokenType>(ModelTokenType.Primitive, argument) });
		}

		private CodeObject BuildClientExecution(TranslationResult result)
		{
			return new CodePrimitiveExpression(result.Script);
		}

		private CodeStatement BuildBindAdapterCall(string methodName, object dataExpr, object indexExpr, object countExpr, bool normalize)
		{
			CodeExpression dataCode, indexCode, countCode;
			this.ProcessArgs(dataExpr, indexExpr, countExpr, out dataCode, out indexCode, out countCode);

			if (dataCode == null || indexCode == null || countCode == null)
			{
				// TODO
				return null;
			}

			#region this.Bind(this.methodName, writer, data, index, count, normalize);

			CodeMethodInvokeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"Bind",
				new CodeMethodReferenceExpression(new CodeThisReferenceExpression(), methodName),
				new CodeArgumentReferenceExpression("writer"),
				dataCode,
				indexCode,
				countCode,
				new CodePrimitiveExpression(normalize));

			#endregion this.Bind(this.methodName, writer, data, index, count, normalize);

			return new CodeExpressionStatement(methodCall);
		}

		#endregion Build Methods

		#region Code Translation Methods

		private CodeExpression TranslateExpression<TResult>(object expr)
		{
			IList<CodeObject> code = this.Translate<TResult>(expr);
			return (code.Count == 1) ? code[0] as CodeExpression : null;
		}

		private IList<CodeObject> Translate<TResult>(object expr)
		{
			List<CodeObject> code = new List<CodeObject>();

			TranslationResult result = new TranslationResult(
				typeof(TResult),
				String.Format(CodeBlockMethodFormat, this.counter),
				new TranslationResult.ParamDefn(typeof(object), "data"),
				new TranslationResult.ParamDefn(typeof(int), "index"),
				new TranslationResult.ParamDefn(typeof(int), "count"))
			{
				Script = this.FormatExpression(expr)
			};

			this.JSBuilder.Translate(result);
			if (result.IsClientOnly)
			{
				// not yet supported on server-side
				code.Add(this.BuildClientExecution(result));
				return code;
			}

			int lineCount = result.LineCount;
			if (lineCount < 1)
			{
				// no method body, return default value of expected type
				var defValue = new CodeDefaultValueExpression(new CodeTypeReference(typeof(TResult)));
				defValue.UserData[Key_EmptyBody] = result.Script;

				code.Add(defValue);
				return code;
			}

			if (lineCount == 1)
			{
				var onlyLine = result.Methods[0].Statements[0];

				CodeMethodReturnStatement rs = onlyLine as CodeMethodReturnStatement;
				if (rs != null)
				{
					if (rs.Expression == null)
					{
						// no method body, return default value of expected type
						var defValue = new CodeDefaultValueExpression(new CodeTypeReference(typeof(TResult)));
						defValue.UserData[Key_EmptyBody] = result.Script;
						code.Add(defValue);
						return code;
					}

					// unwrap return expression
					code.Add(rs.Expression);
					return code;
				}

				if (onlyLine is CodeExpressionStatement)
				{
					// unwrap expression statement
					code.Add(((CodeExpressionStatement)onlyLine).Expression);
					return code;
				}

				if (onlyLine is CodeSnippetStatement)
				{
					// unwrap snippet statement
					code.Add(new CodeSnippetExpression(((CodeSnippetStatement)onlyLine).Value));
					return code;
				}

				// others will remain as a complete method
			}

			this.counter++;

			foreach (var method in result.Methods)
			{
				code.Add(method);
			}

			// return an invokable expression
			code.Add(new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				result.Methods[0].Name,
				new CodeArgumentReferenceExpression("data"),
				new CodeArgumentReferenceExpression("index"),
				new CodeArgumentReferenceExpression("count")));

			return code;
		}

		#endregion Code Translation Methods

		#region Methods

		private CodeExpression EmitExprAsJson(CodeExpression expr)
		{
			#region this.ToJson(writer, expr);

			return new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"ToJson",
				new CodeArgumentReferenceExpression("writer"),
				expr);

			#endregion this.ToJson(writer, expr);
		}

		private CodeStatement EmitVarValue(string varName)
		{
			if (String.IsNullOrEmpty(varName))
			{
				return null;
			}

			#region writer.Write(varName);

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				new CodeVariableReferenceExpression(varName));

			return new CodeExpressionStatement(methodCall);

			#endregion writer.Write(varName);
		}

		private CodeStatement EmitExpression(CodeExpression expr)
		{
			if (expr == null)
			{
				return null;
			}

			CodeDefaultValueExpression defValue = expr as CodeDefaultValueExpression;
			if (defValue != null &&
				defValue.UserData[Key_EmptyBody] != null)
			{
				// empty method body, emit comment
				return new CodeCommentStatement(String.Concat("Empty block: ", defValue.UserData[Key_EmptyBody] as string));
			}

			#region writer.Write(expr);

			CodeExpression methodCall = new CodeMethodInvokeExpression(
				new CodeArgumentReferenceExpression("writer"),
				"Write",
				expr);

			return new CodeExpressionStatement(methodCall);

			#endregion writer.Write(expr);
		}

		private CodeVariableDeclarationStatement AllocateLocalVar<TVar>(string prefix)
		{
			#region TVar prefix_X;

			string locID = String.Format(LocalVarFormat, prefix, ++this.counter);

			return new CodeVariableDeclarationStatement(typeof(TVar), locID);

			#endregion TVar prefix_X;
		}

		private CodeStatement GenerateClientIDVar(out string varName)
		{
			#region string id_XXXX = this.NextID();

			var newLoc = this.AllocateLocalVar<string>("id");

			newLoc.InitExpression = new CodeMethodInvokeExpression(
				new CodeThisReferenceExpression(),
				"NextID");

			varName = newLoc.Name;

			return newLoc;

			#endregion string id_XXXX = this.NextID();
		}

		#endregion Methods

		#region Utility Methods

		private DataName SplitDataName(string name, bool isAttrib)
		{
			string[] parts = (name??"").Split(NameDelim, 2, StringSplitOptions.RemoveEmptyEntries);
			switch (parts.Length)
			{
				case 1:
				{
					return new DataName(parts[0], null, null, isAttrib);
				}
				case 2:
				{
					return new DataName(parts[1], parts[0], null, isAttrib);
				}
				default:
				{
					return new DataName(String.Empty);
				}
			}
		}

		private void SplitTypeName(string fullName, out string typeNS, out string typeName)
		{
			int split = fullName.LastIndexOf('.');
			if (split < 0)
			{
				typeNS = String.Empty;
				typeName = fullName;
				return;
			}

			typeNS = fullName.Substring(0, split);
			typeName = fullName.Substring(split+1);
		}

		#endregion Utility Methods
	}
}
