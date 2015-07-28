﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Gauge.CSharp.Lib.Attribute;
using Gauge.CSharp.Runner.Communication;
using Gauge.Messages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Gauge.CSharp.Runner
{
    public class RefactorHelper
    {
        public static void Refactor(MethodInfo method, IList<ParameterPosition> parameterPositions, IList<string> parameters, string newStepValue)
        {
            var projectFile = Directory.EnumerateFiles(Utils.GaugeProjectRoot, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();

            if (projectFile == null)
            {
                return;
            }
            
            var document = XDocument.Load(projectFile);

            XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";

            var classFiles = document.Descendants(ns + "Project")
                .Where(t => t.Attribute("ToolsVersion") != null)
                .Elements(ns + "ItemGroup")
                .Elements(ns + "Compile")
                .Where(r => r.Attribute("Include") != null)
                .Select(r => Path.GetFullPath(Path.Combine(Utils.GaugeProjectRoot, r.Attribute("Include").Value)));

            Parallel.ForEach(classFiles, (f, state) =>
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(f));
                var root = tree.GetRoot();

                var stepMethods = from node in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    let attributeSyntaxes = node.AttributeLists.SelectMany(syntax => syntax.Attributes)
                    where string.CompareOrdinal(node.Identifier.ValueText, method.Name) == 0
                       && attributeSyntaxes.Any(syntax => string.CompareOrdinal(syntax.ToFullString(), typeof(Step).ToString()) > 0)
                          select node;

                if (!stepMethods.Any()) return;
                
                //Found the method
                state.Break();

                //TODO: check for aliases and error out

                foreach (var methodDeclarationSyntax in stepMethods)
                {
                    var updatedAttribute = ReplaceAttribute(methodDeclarationSyntax, newStepValue);
                    var declarationSyntax = methodDeclarationSyntax.WithAttributeLists(updatedAttribute);
                    var replaceNode = root.ReplaceNode(methodDeclarationSyntax, declarationSyntax);

                    File.WriteAllText(f, replaceNode.ToFullString());
                }
            });
        }

        private static SyntaxList<AttributeListSyntax> ReplaceAttribute(MethodDeclarationSyntax methodDeclarationSyntax, string newStepText)
        {
            var attributeListSyntax = methodDeclarationSyntax.AttributeLists.WithStepAttribute();
            var attributeSyntax = attributeListSyntax.Attributes.GetStepAttribute();
            var attributeArgumentSyntax = attributeSyntax.ArgumentList.Arguments.FirstOrDefault();

            if (attributeArgumentSyntax == null)
            {
                return default(SyntaxList<AttributeListSyntax>);
            }
            var newAttributeArgumentSyntax = attributeArgumentSyntax.WithExpression(
                SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.ParseToken(string.Format("\"{0}\"", newStepText))));

            var attributeArgumentListSyntax = attributeSyntax.ArgumentList.WithArguments(new SeparatedSyntaxList<AttributeArgumentSyntax>().Add(newAttributeArgumentSyntax));
            var newAttributeSyntax = attributeSyntax.WithArgumentList(attributeArgumentListSyntax);

            var newAttributes = attributeListSyntax.Attributes.Remove(attributeSyntax).Add(newAttributeSyntax);
            var newAttributeListSyntax = attributeListSyntax.WithAttributes(newAttributes);

            return methodDeclarationSyntax.AttributeLists.Remove(attributeListSyntax).Add(newAttributeListSyntax);
        }
    }

    public class StepAttributeWalker : CSharpSyntaxRewriter
    {
        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            return base.VisitMethodDeclaration(node);
        }
    }

    public static class AttributeExtensions
    {
        public static AttributeListSyntax WithStepAttribute(this SyntaxList<AttributeListSyntax> list)
        {
            return list.First( syntax => syntax.Attributes.GetStepAttribute()!=null);
        }

        public static AttributeSyntax GetStepAttribute(this SeparatedSyntaxList<AttributeSyntax> list)
        {
            return list.FirstOrDefault(argumentSyntax => 
                string.CompareOrdinal(argumentSyntax.ToFullString(), typeof(Step).ToString()) > 0);
        }
    }
}
