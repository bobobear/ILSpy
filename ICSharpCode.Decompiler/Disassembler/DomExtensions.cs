﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using SRM = System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.Disassembler
{
	public static class MetadataExtensions
	{
		public static void WriteTo(this EntityHandle entity, PEFile module, ITextOutput output, GenericContext genericContext, ILNameSyntax syntax = ILNameSyntax.Signature)
		{
			if (entity.IsNil)
				throw new ArgumentNullException(nameof(entity));
			if (module == null)
				throw new ArgumentNullException(nameof(module));
			var metadata = module.Metadata;
			Action<ILNameSyntax> signature;
			MethodSignature<Action<ILNameSyntax>> methodSignature;
			string memberName;
			switch (entity.Kind) {
				case HandleKind.TypeDefinition: {
					var td = metadata.GetTypeDefinition((TypeDefinitionHandle)entity);
					output.WriteReference(td.GetFullTypeName(metadata).ToILNameString(), new Metadata.TypeDefinition(module, (TypeDefinitionHandle)entity));
					break;
				}
				case HandleKind.TypeReference: {
					var tr = metadata.GetTypeReference((TypeReferenceHandle)entity);
					if (!tr.ResolutionScope.IsNil) {
						output.Write("[");
						var currentTypeRef = tr;
						while (currentTypeRef.ResolutionScope.Kind == HandleKind.TypeReference) {
							currentTypeRef = metadata.GetTypeReference((TypeReferenceHandle)currentTypeRef.ResolutionScope);
						}
						switch (currentTypeRef.ResolutionScope.Kind) {
							case HandleKind.ModuleDefinition:
								var modDef = metadata.GetModuleDefinition();
								output.Write(DisassemblerHelpers.Escape(metadata.GetString(modDef.Name)));
								break;
							case HandleKind.ModuleReference:
								break;
							case HandleKind.AssemblyReference:
								var asmRef = metadata.GetAssemblyReference((AssemblyReferenceHandle)currentTypeRef.ResolutionScope);
								output.Write(DisassemblerHelpers.Escape(metadata.GetString(asmRef.Name)));
								break;
						}
						output.Write("]");
					}
					output.WriteReference(entity.GetFullTypeName(metadata).ToILNameString(), new Metadata.TypeReference(module, (TypeReferenceHandle)entity));
					break;
				}
				case HandleKind.TypeSpecification: {
					var ts = metadata.GetTypeSpecification((TypeSpecificationHandle)entity);
					signature = ts.DecodeSignature(new DisassemblerSignatureProvider(module, output), genericContext);
					signature(syntax);
					break;
				}
				case HandleKind.FieldDefinition: {
					var fd = metadata.GetFieldDefinition((FieldDefinitionHandle)entity);
					signature = fd.DecodeSignature(new DisassemblerSignatureProvider(module, output), new GenericContext(fd.GetDeclaringType(), module));
					signature(ILNameSyntax.SignatureNoNamedTypeParameters);
					output.Write(' ');
					((EntityHandle)fd.GetDeclaringType()).WriteTo(module, output, GenericContext.Empty, ILNameSyntax.TypeName);
					output.Write("::");
					output.WriteReference(DisassemblerHelpers.Escape(metadata.GetString(fd.Name)), new Metadata.FieldDefinition(module, (FieldDefinitionHandle)entity));
					break;
				}
				case HandleKind.MethodDefinition: {
					var md = metadata.GetMethodDefinition((MethodDefinitionHandle)entity);
					methodSignature = md.DecodeSignature(new DisassemblerSignatureProvider(module, output), new GenericContext((MethodDefinitionHandle)entity, module));
					if (methodSignature.Header.HasExplicitThis) {
						output.Write("instance explicit ");
					} else if (methodSignature.Header.IsInstance) {
						output.Write("instance ");
					}
					if (methodSignature.Header.CallingConvention == SignatureCallingConvention.VarArgs) {
						output.Write("vararg ");
					}
					methodSignature.ReturnType(ILNameSyntax.SignatureNoNamedTypeParameters);
					output.Write(' ');
					var declaringType = md.GetDeclaringType();
					if (!declaringType.IsNil) {
						((EntityHandle)declaringType).WriteTo(module, output, genericContext, ILNameSyntax.TypeName);
						output.Write("::");
					}
					bool isCompilerControlled = (md.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.PrivateScope;
					var reference = new Metadata.MethodDefinition(module, (MethodDefinitionHandle)entity);
					if (isCompilerControlled) {
						output.WriteReference(DisassemblerHelpers.Escape(metadata.GetString(md.Name) + "$PST" + MetadataTokens.GetToken(entity).ToString("X8")), reference);
					} else {
						output.WriteReference(DisassemblerHelpers.Escape(metadata.GetString(md.Name)), reference);
					}
					var genericParameters = md.GetGenericParameters();
					if (genericParameters.Count > 0) {
						output.Write('<');
						for (int i = 0; i < genericParameters.Count; i++) {
							if (i > 0)
								output.Write(", ");
							var gp = metadata.GetGenericParameter(genericParameters[i]);
							if ((gp.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) == GenericParameterAttributes.ReferenceTypeConstraint) {
								output.Write("class ");
							} else if ((gp.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == GenericParameterAttributes.NotNullableValueTypeConstraint) {
								output.Write("valuetype ");
							}
							if ((gp.Attributes & GenericParameterAttributes.DefaultConstructorConstraint) == GenericParameterAttributes.DefaultConstructorConstraint) {
								output.Write(".ctor ");
							}
							var constraints = gp.GetConstraints();
							if (constraints.Count > 0) {
								output.Write('(');
								for (int j = 0; j < constraints.Count; j++) {
									if (j > 0)
										output.Write(", ");
									var constraint = metadata.GetGenericParameterConstraint(constraints[j]);
									constraint.Type.WriteTo(module, output, new GenericContext((MethodDefinitionHandle)entity, module), ILNameSyntax.TypeName);
								}
								output.Write(") ");
							}
							if ((gp.Attributes & GenericParameterAttributes.Contravariant) == GenericParameterAttributes.Contravariant) {
								output.Write('-');
							} else if ((gp.Attributes & GenericParameterAttributes.Covariant) == GenericParameterAttributes.Covariant) {
								output.Write('+');
							}
							output.Write(DisassemblerHelpers.Escape(metadata.GetString(gp.Name)));
						}
						output.Write('>');
					}
					output.Write("(");
					for (int i = 0; i < methodSignature.ParameterTypes.Length; ++i) {
						if (i > 0)
							output.Write(", ");
							methodSignature.ParameterTypes[i](ILNameSyntax.SignatureNoNamedTypeParameters);
					}
					output.Write(")");
					break;
				}
				case HandleKind.MemberReference:
					var mr = metadata.GetMemberReference((MemberReferenceHandle)entity);
					memberName = metadata.GetString(mr.Name);
					switch (mr.GetKind()) {
						case MemberReferenceKind.Method:
							methodSignature = mr.DecodeMethodSignature(new DisassemblerSignatureProvider(module, output), genericContext);
							if (methodSignature.Header.HasExplicitThis) {
								output.Write("instance explicit ");
							} else if (methodSignature.Header.IsInstance) {
								output.Write("instance ");
							}
							if (methodSignature.Header.CallingConvention == SignatureCallingConvention.VarArgs) {
								output.Write("vararg ");
							}
							methodSignature.ReturnType(ILNameSyntax.SignatureNoNamedTypeParameters);
							output.Write(' ');
							WriteParent(output, module, metadata, mr.Parent, genericContext, syntax);
							output.Write("::");
							output.WriteReference(DisassemblerHelpers.Escape(memberName), new Metadata.MemberReference(module, (MemberReferenceHandle)entity));
							output.Write("(");
							for (int i = 0; i < methodSignature.ParameterTypes.Length; ++i) {
								if (i > 0)
									output.Write(", ");
								if (i == methodSignature.RequiredParameterCount)
									output.Write("..., ");
								methodSignature.ParameterTypes[i](ILNameSyntax.SignatureNoNamedTypeParameters);
							}
							output.Write(")");
							break;
						case MemberReferenceKind.Field:
							var fieldSignature = mr.DecodeFieldSignature(new DisassemblerSignatureProvider(module, output), genericContext);
							fieldSignature(ILNameSyntax.SignatureNoNamedTypeParameters);
							output.Write(' ');
							WriteParent(output, module, metadata, mr.Parent, genericContext, syntax);
							output.Write("::");
							output.WriteReference(DisassemblerHelpers.Escape(memberName), new Metadata.MemberReference(module, (MemberReferenceHandle)entity));
							break;
					}
					break;
				case HandleKind.MethodSpecification:
					var ms = metadata.GetMethodSpecification((MethodSpecificationHandle)entity);
					var substitution = ms.DecodeSignature(new DisassemblerSignatureProvider(module, output), genericContext);
					switch (ms.Method.Kind) {
						case HandleKind.MethodDefinition:
							var methodDefinition = metadata.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
							var methodName = metadata.GetString(methodDefinition.Name);
							methodSignature = methodDefinition.DecodeSignature(new DisassemblerSignatureProvider(module, output), genericContext);
							if (methodSignature.Header.HasExplicitThis) {
								output.Write("instance explicit ");
							} else if (methodSignature.Header.IsInstance) {
								output.Write("instance ");
							}
							if (methodSignature.Header.CallingConvention == SignatureCallingConvention.VarArgs) {
								output.Write("vararg ");
							}
							methodSignature.ReturnType(ILNameSyntax.SignatureNoNamedTypeParameters);
							output.Write(' ');
							var declaringType = methodDefinition.GetDeclaringType();
							if (!declaringType.IsNil) {
								((EntityHandle)declaringType).WriteTo(module, output, genericContext, ILNameSyntax.TypeName);
								output.Write("::");
							}
							bool isCompilerControlled = (methodDefinition.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.PrivateScope;
							if (isCompilerControlled) {
								output.Write(DisassemblerHelpers.Escape(methodName + "$PST" + MetadataTokens.GetToken(ms.Method).ToString("X8")));
							} else {
								output.Write(DisassemblerHelpers.Escape(methodName));
							}
							output.Write('<');
							for (int i = 0; i < substitution.Length; i++) {
								if (i > 0)
									output.Write(", ");
								substitution[i](syntax);
							}
							output.Write('>');
							output.Write("(");
							for (int i = 0; i < methodSignature.ParameterTypes.Length; ++i) {
								if (i > 0)
									output.Write(", ");
								methodSignature.ParameterTypes[i](ILNameSyntax.SignatureNoNamedTypeParameters);
							}
							output.Write(")");
							break;
						case HandleKind.MemberReference:
							var memberReference = metadata.GetMemberReference((MemberReferenceHandle)ms.Method);
							memberName = metadata.GetString(memberReference.Name);
							methodSignature = memberReference.DecodeMethodSignature(new DisassemblerSignatureProvider(module, output), genericContext);
							if (methodSignature.Header.HasExplicitThis) {
								output.Write("instance explicit ");
							} else if (methodSignature.Header.IsInstance) {
								output.Write("instance ");
							}
							if (methodSignature.Header.CallingConvention == SignatureCallingConvention.VarArgs) {
								output.Write("vararg ");
							}
							methodSignature.ReturnType(ILNameSyntax.SignatureNoNamedTypeParameters);
							output.Write(' ');
							WriteParent(output, module, metadata, memberReference.Parent, genericContext, syntax);
							output.Write("::");
							output.Write(DisassemblerHelpers.Escape(memberName));
							output.Write('<');
							for (int i = 0; i < substitution.Length; i++) {
								if (i > 0)
									output.Write(", ");
								substitution[i](syntax);
							}
							output.Write('>');
							output.Write("(");
							for (int i = 0; i < methodSignature.ParameterTypes.Length; ++i) {
								if (i > 0)
									output.Write(", ");
								methodSignature.ParameterTypes[i](ILNameSyntax.SignatureNoNamedTypeParameters);
							}
							output.Write(")");
							break;
					}
					break;
				case HandleKind.PropertyDefinition:
				case HandleKind.EventDefinition:
					throw new NotSupportedException();
				case HandleKind.StandaloneSignature:
					var standaloneSig = metadata.GetStandaloneSignature((StandaloneSignatureHandle)entity);
					switch (standaloneSig.GetKind()) {
						case StandaloneSignatureKind.Method:
							var methodSig = standaloneSig.DecodeMethodSignature(new DisassemblerSignatureProvider(module, output), genericContext);
							methodSig.ReturnType(ILNameSyntax.SignatureNoNamedTypeParameters);
							output.Write('(');
							for (int i = 0; i < methodSig.ParameterTypes.Length; i++) {
								if (i > 0)
									output.Write(", ");
								methodSig.ParameterTypes[i](ILNameSyntax.SignatureNoNamedTypeParameters);
							}
							output.Write(')');
							break;
						case StandaloneSignatureKind.LocalVariables:
						default:
							throw new NotSupportedException();
					}
					break;
				default:
					throw new NotSupportedException();
			}
		}

		static void WriteParent(ITextOutput output, PEFile module, MetadataReader metadata, EntityHandle parentHandle, GenericContext genericContext, ILNameSyntax syntax)
		{
			switch (parentHandle.Kind) {
				case HandleKind.MethodDefinition:
					var methodDef = metadata.GetMethodDefinition((MethodDefinitionHandle)parentHandle);
					((EntityHandle)methodDef.GetDeclaringType()).WriteTo(module, output, genericContext, syntax);
					break;
				case HandleKind.ModuleReference:
					output.Write('[');
					var moduleRef = metadata.GetModuleReference((ModuleReferenceHandle)parentHandle);
					output.Write(metadata.GetString(moduleRef.Name));
					output.Write(']');
					break;
				case HandleKind.TypeDefinition:
				case HandleKind.TypeReference:
				case HandleKind.TypeSpecification:
					parentHandle.WriteTo(module, output, genericContext, syntax);
					break;
			}
		}
	}
}