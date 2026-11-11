using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ObFramework.Generator.Unity
{
    /// <summary>
    /// Small adapter over <see cref="SourceProductionContext"/> plus the current compilation so the
    /// incremental generator pipeline and the shared emission helpers agree on one context type.
    /// </summary>
    internal readonly struct GenerationContext
    {
        private readonly SourceProductionContext _output;

        public GenerationContext(SourceProductionContext output, Compilation compilation)
        {
            _output = output;
            Compilation = compilation;
        }

        public Compilation Compilation { get; }

        public CancellationToken CancellationToken => _output.CancellationToken;

        public void AddSource(string hintName, SourceText text) => _output.AddSource(hintName, text);

        public void ReportDiagnostic(Diagnostic diagnostic) => _output.ReportDiagnostic(diagnostic);
    }
}
