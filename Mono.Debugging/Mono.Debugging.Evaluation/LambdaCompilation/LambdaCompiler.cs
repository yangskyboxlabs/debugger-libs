using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ICSharpCode.NRefactory.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace Mono.Debugging.Evaluation.LambdaCompilation
{
    public abstract class LambdaCompiler<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly EvaluationContext Ctx;
        readonly NRefactoryExpressionEvaluatorVisitor<TType, TValue> Visitor;
        readonly HashSet<string> variableNames = new HashSet<string>();
        protected LambdaHelperInstance<TType, TValue> LambdaHelper { get; private set; }
        protected ObjectValueAdaptor<TType, TValue> Adapter { get; private set; }

        protected LambdaCompiler(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            NRefactoryExpressionEvaluatorVisitor<TType, TValue> visitor)
        {
            if (ctx == null || visitor == null)
                throw new ArgumentNullException();
            this.Ctx = ctx;
            this.Visitor = visitor;
        }

        protected CompilationResult<TType, TValue> Compile()
        {
            if (!Ctx.Options.AllowLambdaEvaluation)
                throw new EvaluatorException("Need a version of the .Net Framework no less than 4.6", Array.Empty<object>());
            return DoCompile();
        }

        CompilationResult<TType, TValue> DoCompile()
        {
            //this.LambdaHelper = this.Adapter.Session.GetDebuggingHelper(this.Ctx, this.Adapter).CreateLambdaHelperInstance(Ctx);
            Microsoft.CodeAnalysis.SyntaxTree tree = CreateTree();
            CSharpCompilation csharpCompilation = CompileTree(tree);
            if (IsOccurredAmbig(csharpCompilation.GetDiagnostics()))
                csharpCompilation = CompileTree(RemoveSysLinq(tree));
            EmitResult emitResult = EmitCompilation(csharpCompilation, out var assembly);

            //this.Logger.Trace(string.Format("Lambdas compiled with result: {0}", emitResult.Success));
            if (!emitResult.Success)
                return new CompilationResult<TType, TValue>(emitResult.Diagnostics.Where(x => x.WarningLevel == 0).Select(x => x.GetMessage()).ToArray());
            Dictionary<LambdaExpression, ValueReference<TType, TValue>> lambdas = CalculateLambdas(assembly, csharpCompilation);

            //this.Logger.Trace("Lambdas calculated");
            return new CompilationResult<TType, TValue>(lambdas);
        }

        static Microsoft.CodeAnalysis.SyntaxTree RemoveSysLinq(Microsoft.CodeAnalysis.SyntaxTree tree)
        {
            IEnumerable<UsingDirectiveSyntax> usingDirectiveSyntaxes = tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Where(x => x.ToString().Equals("using System.Linq;"));
            return tree.GetRoot().RemoveNodes(usingDirectiveSyntaxes, SyntaxRemoveOptions.KeepNoTrivia).SyntaxTree;
        }

        static bool IsOccurredAmbig(ImmutableArray<Diagnostic> diagnostics)
        {
            return diagnostics.Where(x => x.WarningLevel == 0).FirstOrDefault(x => x.Id.Equals("CS0121")) != null;
        }

        EmitResult EmitCompilation(Compilation compilation, out byte[] assembly)
        {
            assembly = null;
            using (var memoryStream = new MemoryStream())
            {
                EmitResult emitResult = compilation.Emit(memoryStream);
                if (emitResult.Success)
                    assembly = memoryStream.ToArray();
                return emitResult;
            }
        }

        protected ValueReference<TType, TValue> GetValue(string name)
        {
            return Adapter.DebuggerSession.Evaluators.GetEvaluator(Ctx).Evaluate(Ctx, name);
        }

        Dictionary<LambdaExpression, ValueReference<TType, TValue>> CalculateLambdas(
            byte[] assembly,
            Compilation compilation)
        {
            LambdaHelper.LoadAssemblyAndPrepareLambdas(assembly);

            //this.Logger.Trace("Assembly with lambdas loaded in target process");
            Tuple<string[], TValue[]> variableNamesAndValues = GetVariableNamesAndValues();
            LambdaHelper.SetValues(variableNamesAndValues.Item1, variableNamesAndValues.Item2);
            return GetLambdas(compilation);
        }

        Tuple<string[], TValue[]> GetVariableNamesAndValues()
        {
            List<string> list1 = variableNames.ToList();
            List<TValue> list2 = variableNames.Select((Func<string, TValue>)(x => this.GetValue(x).Value)).ToList();
            if (!string.IsNullOrEmpty(this.thisReferenceName))
            {
                ValueReference<TContext, TType, TValue> thisReference = this.Adapter.GetThisReference(this.Ctx);
                if (thisReference == null)
                    throw new EvaluatorException("'this' reference not available in the current evaluation context.", Array.Empty<object>());
                list1.Add(this.thisReferenceName);
                list2.Add(thisReference.Value);
            }

            return new Tuple<string[], TValue[]>(list1.ToArray(), list2.ToArray());
        }

        Microsoft.CodeAnalysis.SyntaxTree CreateTree()
        {
            StringBuilder builder = new StringBuilder();
            GenerateUsingNamespaces(builder, (IEnumerable<string>)Ctx.Adapter.GetImportedExtensionMethodNamespaces(Ctx));
            SaveCapturedObjectNames();
            GenerateClass(builder);
            return this.ReplaceThisReferenceIfNeeded(CSharpSyntaxTree.ParseText(builder.ToString(), (CSharpParseOptions)null, "", (Encoding)null, new CancellationToken()));
        }

        CSharpCompilation CompileTree(Microsoft.CodeAnalysis.SyntaxTree tree)
        {
            IEnumerable<PortableExecutableReference> executableReferences = Ctx.Adapter.GetMetadataReferencesForRoslyn(this.Ctx).SelectNotNull<MetadataReferenceForRoslyn, PortableExecutableReference>((Func<MetadataReferenceForRoslyn, PortableExecutableReference>)(x =>
            {
                if (x.AssemblyMetadata is AssemblyMetadata assemblyMetadata)
                {
                    ImmutableArray<string> aliases = new ImmutableArray<string>();
                    return assemblyMetadata.GetReference((DocumentationProvider)null, aliases, false, (string)null, (string)null);
                }

                string path = (string)null;
                try
                {
                    path = x.Location.FullPath;
                    MemoryStream memoryStream = new MemoryStream(FileSystemPath.TryParse(path, FileSystemPathInternStrategy.INTERN).ReadAllBytes());
                    x.MetadataStream = (Stream)memoryStream;
                    AssemblyMetadata fromStream = AssemblyMetadata.CreateFromStream((Stream)memoryStream, false);
                    x.AssemblyMetadata = (object)fromStream;
                    return fromStream.GetReference((DocumentationProvider)null, new ImmutableArray<string>(), false, (string)null, (string)null);
                }
                catch (Exception ex)
                {
                    this.Logger.Error(ex, "Failed to load MetadataReference for \"" + path + "\"");
                }

                return (PortableExecutableReference)null;
            }));
            CSharpCompilationOptions importOptionsAll = LambdaCompiler.GetOptionsWithMetadataImportOptionsAll(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, false, (string)null, (string)null, (string)null, (IEnumerable<string>)null, OptimizationLevel.Debug, false, false, (string)null, (string)null, new ImmutableArray<byte>(), new bool?(), Microsoft.CodeAnalysis.Platform.AnyCpu, ReportDiagnostic.Default, 4, (IEnumerable<KeyValuePair<string, ReportDiagnostic>>)null, true, false, (XmlReferenceResolver)null, (SourceReferenceResolver)null, (MetadataReferenceResolver)null, (AssemblyIdentityComparer)null, (StrongNameProvider)null, false, MetadataImportOptions.Public));
            return CSharpCompilation.Create(this.CreateUniqueAssemblyName(), (IEnumerable<Microsoft.CodeAnalysis.SyntaxTree>)new Microsoft.CodeAnalysis.SyntaxTree[1]
            {
                tree
            }, (IEnumerable<MetadataReference>)executableReferences, (CSharpCompilationOptions)null).WithOptions(importOptionsAll);
        }

        static void GenerateUsingNamespaces(StringBuilder builder, IEnumerable<string> namespaces)
        {
            foreach (string str in namespaces)
                builder.AppendFormat("using {0};\n", str);
            builder.AppendFormat("using {0};\n", "System.Linq");
        }

        protected abstract Dictionary<LambdaExpression, ValueReference<TType, TValue>> GetLambdas(Compilation compilation);
    }
}
