# VBMigrator — Contexto del Proyecto

## Estado actual (2026-07-15)

- **Spec:** aprobado (v6, 6 rondas de review). Listo para implementar. → `docs/spec.md`
- **Plan:** completo, 26 tareas TDD. → `docs/plan.md`
- **Código:** NO iniciado aún. Esta sesión debe comenzar desde Task 1.

## Resumen ejecutivo

VBMigrator es una herramienta de migración VB.NET → C# con pipeline híbrido Roslyn + LLM.

**Componentes:**
```
VBMigrator.Core   (net8.0)  — toda la lógica de negocio
VBMigrator.CLI    (net8.0)  — dotnet global tool; API boundary
VBMigrator.VSIX   (net472)  — thin launcher + UI; NO referencia Core
Tests             (net8.0)
```

**Regla fundamental:** VSIX no referencia Core. Lo invoca como proceso hijo via CLI.

## Decisiones clave (no volver a debatir)

| Decisión | Elección | Razón |
|---------|---------|-------|
| ICSharpCode granularidad | whole-file first (Opción A) | No hay API pública para snippet |
| VSIX integración | out-of-process CLI child | net472 ↔ net8 isolation |
| SeedRuleEngine scope | method-level SyntaxNode ONLY | File/type-level va a ICSharpCode |
| ConfidenceScorer | min(nodeConfidences) | Conservador, previene approvals silenciosos |
| ConvertFile output | JSON stdout con --json-output | CamelCase + JsonStringEnumConverter |
| ConvertSolution output | SQLite + stderr progress | VSIX lee exit code + "FILE: x OK\|FAIL\|HUMAN_QUEUE" |
| LlmUsingResolver posición | DESPUÉS de SeedRules + LLM | Debe ver tipos introducidos por ambos |

## 25 SeedRules activas (Method-level ONLY)

Las siguientes 4 fueron REMOVIDAS del catálogo (ISeedRule no puede manejarlas):
- `with_block` → ICSharpCode (state between nodes)
- `withevents_handler` → ICSharpCode (class-level context)
- `module_to_static` → ICSharpCode (type-level node)
- `imports_static` → ICSharpCode (file-level node)

Activas: my_settings, my_filesystem_read, my_filesystem_write, my_app_version, my_user, on_error_goto, on_error_resume, like_operator, exponentiation, cint_bool, string_concat, integer_division, is_nothing, isnot_nothing, andalso, orelse, byval_param, optional_param, redim_preserve, date_literal, erase_array, nothing_assign_valuetype, string_comparison_case, for_byte_overflow, iif_function

## ISeedRule interface

```csharp
public interface ISeedRule
{
    string Tag { get; }
    int Priority { get; }
    bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null);
    SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null);
}
```

## Pipeline §4.1 (orden exacto)

```
[1] ProjectFileConverter (.vbproj → .csproj)
[2] ICSharpCode whole-file VB → initial_cs
[3] Analyzer → DifficultyMap (flags por método)
[4] Split: emparejar cs_method ↔ vb_method
[5] Evaluate: sin flags → confidence 0.85, skip a [8]
    Con flags → continuar:
[6] DB lookup (flag→tag: OnError→on_error_goto, OnErrorResume→on_error_resume,
    LikeOp→like_operator, ExponOp→exponentiation, ByteLoop→for_byte_overflow,
    MyNamespace→my_*, LateBinding→skip)
[7] SeedRuleEngine → LLM → LlmUsingResolver (en ese orden, post ambos)
[8] Validator (Roslyn compile) → RepairAgent si falla → HumanQueue si falla x2
[9] ConfidenceScorer min() → AUTO(≥0.85) / SUGGEST(0.65-0.84) / HUMAN(<0.65)
```

## Comportamientos especiales (NO cambiar sin spec update)

- `on_error_goto` + `GotoCrossBlock` flag → `CanHandle` returns false → HumanQueue CROSSBLOCK_GOTO
- `iif_function` → SIEMPRE agrega `// ⚠ VBMigrator: IIf evalúa ambos brazos en VB; ternario ?: no lo hace`
- `string_concat` + operando numérico → SUGGEST confidence 0.70 (no AUTO)
- `nothing_assign_valuetype` sin semanticModel → `CanHandle` false → va a LLM
- `for_byte_overflow` → HumanQueue + flag BYTE_LOOP_OVERFLOW
- `on_error_resume` → HumanQueue + comentario WARN (no per-statement wrapping en MVP)
- RepairAgent penalty: -0.10 confidence si necesita repair
- PatternNormalizer upsert: WHERE tag=@tag AND vb_template=@normalizedTemplate (NO LIKE)

## CliRunner deadlock fix (IMPORTANTE)

```csharp
var stdoutTask = proc.StandardOutput.ReadToEndAsync();
var stderrTask  = proc.StandardError.ReadToEndAsync();
await proc.WaitForExitAsync();
var json    = await stdoutTask;
var _errors = await stderrTask;  // nunca ignorar la Task
```

## JSON contract

- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `JsonStringEnumConverter` (enums como strings, no enteros)
- VSIX `TranslationResultDto`: `route` y `llmFailureReason` como `string` (no enum)

## PatternNormalizer

- VB-origin identifiers → `__var1__`, `__var2__`
- C#-introduced identifiers → `__new1__`, `__new2__`
- Types → `__Type1__`, `__Type2__`
- Mismo mapping VB↔CS para variables compartidas
- Algoritmo determinista

## CliLocator orden de búsqueda

1. Tools → VBMigrator → Settings path configurado
2. PATH del sistema
3. Error con mensaje de instalación

## SampleVBProject restricciones

- VB library/consola (SIN WebForms, SIN System.Web)
- ASPX tests: parse only, NO compilar output

## Self-Review gaps del plan (implementador debe resolver)

El plan incluye una sección Self-Review al final con:
1. `SeedRuleRegistry.GetAll()` — código inline en Self-Review
2. `PipelineFactory.cs` — wire up TranslationPipeline desde config
3. `SeedRuleEngine.TryConvert(SyntaxNode, SemanticModel?, out SyntaxNode?, out double)` — firma exacta
4. `RoslynCompileValidator.ValidateAsync` → `ValidationResult` con `Success`/`Errors`

## Cómo retomar

1. Leer `docs/plan.md` completo
2. Usar skill `superpowers:subagent-driven-development` (recomendado) o `superpowers:executing-plans`
3. El código va en `src/` dentro de este directorio (crear `VBMigrator.sln` aquí)
4. Task 1 del plan define la estructura de solución completa

## Archivos de referencia

- `docs/spec.md` — spec completo aprobado (v6)
- `docs/plan.md` — plan de implementación (26 tasks, TDD)
