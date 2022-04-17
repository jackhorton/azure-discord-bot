//#define DEBUG_GENERATOR
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
#if DEBUG_GENERATOR
using System.Diagnostics;
#endif
using System.Linq;
using System.Text;

namespace AzureBot.CommandLine.SourceGenerator
{
    [Generator]
    public class CommandGenerator : ISourceGenerator
    {
        private const string _attributeSource = @"
using System;
using System.Diagnostics;
namespace AzureBot.CommandLine
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    [Conditional(""CommandGenerator_DEBUG"")]
    public sealed class GeneratedCommandAttribute : Attribute
    {
        public GeneratedCommandAttribute(string command, string description)
        {
            Command = command;
            Description = description;
        }

        public string Command { get; }
        public string Description { get; }
        public bool GenerateFactory { get; set; } = true;
    }
}
";

        public void Execute(GeneratorExecutionContext context)
        {
#if DEBUG_GENERATOR
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }
#endif

            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            var types = new CompilationTypes(context);

            foreach (var classSymbol in receiver.Classes)
            {
                var sourceText = GetSourceForClass(classSymbol, types);
                if (!(sourceText is null))
                {
                    context.AddSource($"{classSymbol.Name}.g.cs", sourceText);
                }
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization((ctx) => ctx.AddSource("CommandAttributes.g.cs", _attributeSource));
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        private SourceText GetSourceForClass(INamedTypeSymbol classSymbol, CompilationTypes types)
        {
            var attribute = classSymbol.GetAttributes().SingleOrDefault((a) => a.AttributeClass.Equals(types.GeneratedCommandAttributeType, SymbolEqualityComparer.Default));
            if (attribute is null)
            {
                return null;
            }

            var namedArguments = attribute.NamedArguments.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value);
            if (namedArguments.TryGetValue("GenerateFactory", out var generateFactory) && (bool)generateFactory.Value == false)
            {
                return null;
            }

            var command = attribute.ConstructorArguments[0].Value as string;
            var description = attribute.ConstructorArguments[1].Value as string;

            var properties = classSymbol
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Where((p) =>
                {
                    if (!p.IsStatic || !(p.Type is INamedTypeSymbol fieldType) || !fieldType.IsGenericType)
                    {
                        return false;
                    }

                    var unboundType = fieldType.ConstructedFrom;
                    return unboundType.Equals(types.OptionType, SymbolEqualityComparer.Default) || unboundType.Equals(types.ArgumentType, SymbolEqualityComparer.Default);
                });
            var collectionInitializer = string.Join(",", properties.Select(p => p.Name));
            return SourceText.From($@"
using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;

namespace {classSymbol.ContainingNamespace.ToDisplayString()}
{{
    public partial class {classSymbol.Name}
    {{
        public static Command GetCommand(IServiceProvider sp)
        {{
            var command = new Command(""{command}"", ""{description}"") {{ {collectionInitializer} }};
            command.Handler = ActivatorUtilities.CreateInstance<{classSymbol.Name}>(sp);
            return command;
        }}
    }}
}}
", Encoding.UTF8);
        }

        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            private readonly List<INamedTypeSymbol> _classes = new List<INamedTypeSymbol>();

            public IReadOnlyCollection<INamedTypeSymbol> Classes => _classes;

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is ClassDeclarationSyntax classDeclaration && context.SemanticModel.GetDeclaredSymbol(context.Node) is INamedTypeSymbol classSymbol)
                {
                    var hasAttribute = classSymbol.GetAttributes().Any((a) => a.AttributeClass.ToDisplayString() == "AzureBot.CommandLine.GeneratedCommandAttribute");
                    var isPartial = classDeclaration.Modifiers.Any((mod) => mod.IsKind(SyntaxKind.PartialKeyword));
                    if (hasAttribute && isPartial)
                    {
                        _classes.Add(classSymbol);
                    }
                }
            }
        }

        private class CompilationTypes
        {
            public INamedTypeSymbol CommandType { get; }
            public INamedTypeSymbol OptionType { get; }
            public INamedTypeSymbol ArgumentType { get; }
            public INamedTypeSymbol GeneratedCommandAttributeType { get; }

            public CompilationTypes(GeneratorExecutionContext context)
            {
                OptionType = AssertCompilationType(context, "System.CommandLine.Option`1");
                ArgumentType = AssertCompilationType(context, "System.CommandLine.Argument`1");
                CommandType = AssertCompilationType(context, "System.CommandLine.Command");
                GeneratedCommandAttributeType = AssertCompilationType(context, "AzureBot.CommandLine.GeneratedCommandAttribute");
            }

            private INamedTypeSymbol AssertCompilationType(GeneratorExecutionContext context, string typeName)
            {
                return context.Compilation.GetTypeByMetadataName(typeName) ?? throw new ArgumentException($"Could not find compilation type for '{typeName}'");
            }
        }
    }
}
