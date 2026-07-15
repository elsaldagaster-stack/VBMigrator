# VBMigrator — Spec de Diseño

**Fecha:** 2026-07-14 (revisado post-review)
**Estado:** Aprobado para implementación (revisado post-review v6)
**Destino:** Uso interno primero, comercial después
**Stack principal:** VS Extension (VSIX out-of-process) + CLI + Core net8.0

---

## 1. Problema

No existe herramienta que cubra migración completa de VB.NET → C# incluyendo:
- Archivos `.aspx` con code-behind VB (directivas, event wiring)
- `My` namespace mapeado a BCL equivalentes
- `On Error GoTo` y `On Error Resume Next`
- Operadores VB sin equivalente directo (`Like`, `^`)
- Aprendizaje de correcciones humanas para mejorar traducciones futuras

Microsoft no provee guía oficial de conversión de lenguaje. Herramientas existentes dejan entre 10–30% de código sin traducir o con bugs semánticos silenciosos.

---

## 2. Solución

**VBMigrator**: herramienta de migración VB.NET → C# con pipeline híbrido (Roslyn + LLM) y base de conocimiento entrenable por correcciones humanas.

### Componentes

```
VBMigrator.Core   (net8.0)  — toda la lógica de negocio
VBMigrator.CLI    (net8.0)  — dotnet global tool; es la API boundary
VBMigrator.VSIX   (net472)  — thin launcher + UI; NO referencia Core
Tests             (net8.0)
```

---

## 3. Arquitectura

```
┌─────────────────────────────────────────────────────────────┐
│                  VBMigrator.Core (net8.0)                   │
│  Analyzer · Translator · Validator · Learning               │
│  SeedRules · AspxHandler · ProjectFileConverter             │
└────────────────────────┬────────────────────────────────────┘
                         │ referencia directa
               ┌─────────┴─────────┐
               ▼                   ▼
      VBMigrator.CLI          (sin referencia a Core)
      (net8.0)                VBMigrator.VSIX (net472)
      wrapper fino +          lanza CLI como proceso hijo
      JSON output mode        comunica vía JSON stdout
```

**Regla fundamental:** VSIX no referencia Core ni CLI como assembly. Lo invoca como proceso:
```
vbmigrator convert --file foo.vb --json-output
vbmigrator kb save --vb "..." --cs "..." --tag "on_error_goto"
```

Esto permite que Core y CLI usen net8.0 sin restricciones de target. VSIX sigue siendo net472 (requerido por VS SDK) pero es solo un launcher con UI.

---

## 4. Pipeline de Traducción

### 4.1 Flujo por archivo `.vb`

**Decisión de arquitectura — granularidad ICSharpCode:** Opción A (whole-file first).
ICSharpCode.CodeConverter opera a nivel de archivo completo (no existe API pública para snippet de método). El pipeline convierte el archivo completo primero, luego post-procesa por método.

```
INPUT archivo .vb
    │
    ▼
[1. ProjectFileConverter] — .vbproj → .csproj (ItemGroups, refs)
    │
    ▼
[2. ICSharpCode.CodeConverter] — conversión whole-file VB → initial_cs
    │  await CodeConverter.ConvertAsync(vbProject, options)
    │  Opera a nivel de archivo completo — no snippets de método
    │  Output: initial_cs (C# inicial, puede tener bugs en patrones problemáticos)
    │
    ▼
[3. Analyzer] — Roslyn sobre VB original, genera DifficultyMap
    │           Flags por método: WithEvents, OptionStrictOff, MyNamespace,
    │           OnError, LateBinding, LikeOp, ExponOp,
    │           GotoCrossBlock, ByteLoop, OnErrorResume
    │
    ▼
[4. Split por función] — Roslyn parsea initial_cs + VB original
    │  Empareja método C# ↔ método VB:
    │    nombre igual         → match directo
    │    Sub New              → match contra C# constructor (por kind)
    │    Overloads            → match por nombre + aridad (param count)
    │    Operators            → token mapping (Operator + → operator+)
    │    Sin match            → confidence 0.85 (confía en ICSharpCode, sin re-traducción)
    │                           Riesgo aceptado: bugs semánticos silenciosos de ICSharpCode
    │                           no detectados — ver §10.3
    │  Unidad de trabajo = par (cs_method, vb_method)
    │
    ▼ (por función, paralelizable)
[5. Evaluación por método]
    │  Sin flags problemáticos en DifficultyMap →
    │    usar initial_cs del método directamente, confidence 0.85
    │    → saltar a [8. Validator]
    │  Con flags → re-traducir desde VB original, continuar:
    │
    ▼
[6. Correction DB Lookup — MVP: exact tag match]
    │
    │  FASE 1 (MVP):
    │    Tag derivado desde DifficultyMap.Functions[method].Flags (mapeo flag→tag):
    │      OnError (sin GotoCrossBlock) → "on_error_goto"
    │      OnErrorResume              → "on_error_resume"
    │      LikeOp                    → "like_operator"
    │      ExponOp                   → "exponentiation"
    │      ByteLoop                  → "for_byte_overflow"
    │      MyNamespace               → "my_*" (primer my_* que matchee)
    │      LateBinding               → tag vacío → skip lookup
    │    Múltiples flags: usar flag de mayor Priority (según orden SeedRules en §5)
    │    Query: SELECT * FROM patterns WHERE tag = @derivedTag LIMIT 1
    │    Match → inyectar como few-shot al LLM
    │    No match → solo seed rules + LLM sin few-shot
    │
    │  FASE 2 (con embeddings):
    │    similarity >= 0.75 → aplicar patrón guardado directamente
    │    0.50–0.74 → inyectar como few-shot
    │    < 0.50 → solo seed rules
    │
    │  Nota: PatternNormalizer NO aparece aquí — solo en §6.1 (Learning Loop al guardar corrección)
    │
    ▼
[7. Re-traducción desde VB original]
    │  SeedRuleEngine (match en SyntaxNode VB, 25 reglas activas, por Priority desc)
    │    → primera regla que hace match en nodo gana; confidence 0.90
    │    → reglas composables: varios nodos del mismo método procesados
    │  LLM (Claude Sonnet 4.6) para nodos no cubiertos por SeedRules
    │    → fallback LLM: 2 retries con backoff para RateLimit
    │    → fallo LLM definitivo: HumanQueue con FailureReason
    │  LlmUsingResolver — sobre C# completo del método (post SeedRules + LLM)
    │    → ver §4.3; resuelve tipos introducidos por ambas traducciones juntas
    │
    ▼
[8. Validator] — Roslyn in-memory compile
    │  FAIL → RepairAgent (LLM call con error context) → retry 1x
    │  FAIL x2 → confidence = 0.0, HumanQueue
    │
    ▼
[9. ConfidenceScorer]
    │  Agregación: min(confidence de todos los nodos del método)
    │    Conservador — un nodo con baja confianza baja todo el método
    │    Previene approvals silenciosos con nodos problemáticos ocultos
    │  >= 0.85 → AUTO (escribe .cs)
    │  0.65–0.84 → SUGGEST (Review Queue, secciones inciertas resaltadas)
    │  < 0.65 → HUMAN QUEUE (revisión completa requerida)
```

### 4.2 Flujo ASPX — pipeline semi-paralelo

El pipeline ASPX **no es completamente independiente**: comparte el `VBSyntaxTree` generado por el Analyzer (paso [3] del pipeline VB). Inicia una vez que el Analyzer completa, luego corre en paralelo con el procesamiento por método (pasos [4]-[9]).

```
[3. Analyzer completa] → VBSyntaxTree disponible
    │
    ├─→ [Pipeline VB continúa: paso [4] Split ...]
    │
    └─→ [Pipeline ASPX — en paralelo con pasos [4]-[9] del VB pipeline]
            │
            ├─→ AspxDirectiveRewriter (sobre .aspx)
            │      Language="vb" → Language="C#"
            │      CodeBehind="x.aspx.vb" → "x.aspx.cs"
            │
            └─→ EventWireupMigrator
                   Input: VBSyntaxTree (del Analyzer, sin re-parseo) + archivo .aspx
                   Lee declaraciones Handles del VBSyntaxTree
                   Detecta AutoEventWireup="false"
                   → genera event subscriptions en Page_Init del .aspx.cs
```

NOTA: Inline VB en markup (`<% %>`, `<%= %>`) → Phase 2

### 4.3 LlmUsingResolver — resolución de using statements post-LLM

El `SystemPrompt` impide que el LLM genere `using` statements. Pero el C# traducido puede referenciar tipos cuyo namespace no está importado (e.g., `WindowsIdentity` si LLM traduce `My.User.Name`). `LlmUsingResolver` resuelve esto post-traducción y post-SeedRule:

```csharp
// Translator/LlmUsingResolver.cs
public class LlmUsingResolver
{
    private static readonly Dictionary<string, string> _wellKnown = new()
    {
        ["WindowsIdentity"] = "System.Security.Principal",
        ["Regex"]           = "System.Text.RegularExpressions",
        ["Assembly"]        = "System.Reflection",
        ["DateTime"]        = "System",
        // tipos introducidos por SeedRules o LLM documentados aquí
    };

    // Roslyn identifica tipos no resueltos en el C# generado del método.
    // _wellKnown → agrega using al file-level context acumulado.
    // Tipo no encontrado en tabla → HumanQueue con flag MISSING_USING.
    public IEnumerable<string> Resolve(SyntaxTree csTree, SemanticModel? model);
}
```

`TranslationPipeline` agrega los usings resueltos al archivo `.cs` destino al finalizar el método.

### 4.4 Tipos del dominio

```csharp
// Models/TranslationRoute.cs
public enum TranslationRoute { SeedRule, Llm, HumanQueue, Error }

// Models/TranslationRequest.cs
public record TranslationRequest
{
    public required string VbSource { get; init; }
    public required string FilePath { get; init; }
    public string? MethodName { get; init; }
}

// Models/TranslationResult.cs
public record TranslationResult
{
    public required string CsSource { get; init; }
    public double Confidence { get; init; }
    public TranslationRoute Route { get; init; }
    public bool CompilerPassed { get; init; }
    public List<string> CompilerErrors { get; init; } = new();
    public string? PatternTag { get; init; }
    public LlmFailureReason? LlmFailureReason { get; init; }
}

// Models/LlmFailureReason.cs
public enum LlmFailureReason { RateLimit, Timeout, ApiError, ContentFilter }

// Models/DifficultyMap.cs
public record DifficultyMap
{
    public required string FilePath { get; init; }
    public int OverallScore { get; init; }           // 0-100
    public List<FunctionDifficulty> Functions { get; init; } = new();
}

public record FunctionDifficulty
{
    public required string MethodName { get; init; }
    public int Score { get; init; }
    public List<string> Flags { get; init; } = new();
    public TranslationRoute Route { get; init; }
}

// Models/Pattern.cs
public class Pattern
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Tag { get; set; }
    public required string VbTemplate { get; set; }
    public required string CsTemplate { get; set; }
    public string Source { get; set; } = "seed";    // seed | human | verified_auto
    public int Applied { get; set; }
    public int Successes { get; set; }
    public byte[]? Embedding { get; set; }          // NULL en Phase 1
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public double Confidence
    {
        get
        {
            var base_ = Source switch { "seed" => 0.90, "human" => 0.70, _ => 0.80 };
            if (Applied == 0) return base_;
            return Math.Min(1.0, base_ + (double)Successes / Applied * 0.30);
        }
    }
}

// Models/TranslationLog.cs
public class TranslationLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? PatternId { get; set; }
    public required string FilePath { get; set; }
    public string? MethodName { get; set; }
    public required string VbInput { get; set; }
    public required string CsOutput { get; set; }
    public bool WasCorrected { get; set; }
    public string? HumanCs { get; set; }
    public bool CompilerPassed { get; set; }
    public double Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### 4.5 RepairAgent

```
Error Roslyn: "CS0246: tipo 'XYZ' no encontrado"
    │
    ▼
LLM call (sin retry — RepairAgent usa token budget mínimo):
  System: "Eres repair agent. Fix SOLO el error indicado. No refactorices. Devuelve solo el bloque C# corregido."
  User:   [error_message] + [líneas afectadas ± 3] (no el archivo completo)
    │
    ▼
Re-compile → pasa: confidence ajustado -0.10 (necesitó repair)
           → falla: confidence = 0.0 → HumanQueue
```

---

## 5. Seed Rules — Catálogo (25 reglas activas)

**Interfaz:**
```csharp
public interface ISeedRule
{
    string Tag { get; }
    int Priority { get; }           // mayor Priority = se evalúa primero; default 100
    bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null);
    SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null);
}
```

Reglas puramente sintácticas ignoran `semanticModel`. Reglas que lo requieren (`nothing_assign_valuetype`, `string_concat` con tipo numérico) lo usan cuando está disponible; si es `null` (snippet sin proyecto), aplican comportamiento conservador (ver notas por regla en la tabla).

**SeedRuleEngine:** aplica reglas en orden descendente de Priority sobre nodos VB originales (Roslyn VB parser). Primera regla que hace match en un nodo gana. Reglas se componen para diferentes nodos del mismo método. Pasa `null` como `semanticModel` cuando no hay proyecto cargado.

**Catálogo:**

| Tag | VB.NET input | C# output | Notas |
|-----|-------------|-----------|-------|
| `my_settings` | `My.Settings.Foo` | `Properties.Settings.Default.Foo` | |
| `my_filesystem_read` | `My.Computer.FileSystem.ReadAllText(p)` | `System.IO.File.ReadAllText(p)` | |
| `my_filesystem_write` | `My.Computer.FileSystem.WriteAllText(p, s)` | `System.IO.File.WriteAllText(p, s)` | |
| `my_app_version` | `My.Application.Info.Version` | `Assembly.GetExecutingAssembly().GetName().Version` | |
| `my_user` | `My.User.Name` | `WindowsIdentity.GetCurrent().Name` | |
| `on_error_goto` | `On Error GoTo Label` ... `Label:` ... `Err.Description` | `try { } catch (Exception ex) { }` con `ex.Message` donde estaba `Err.Description` | Priority 150. **Interacción con GotoCrossBlock:** si el Analyzer detecta flag `GotoCrossBlock` para este método, `CanHandle` devuelve `false` — try/catch simple es semánticamente incorrecto cuando GoTo cruza bloques. El método va a **HumanQueue** con flag `CROSSBLOCK_GOTO`. Ninguna otra regla maneja este caso. |
| `on_error_resume` | `On Error Resume Next` | HumanQueue + comentario `// ⚠ VBMigrator: On Error Resume Next — requiere revisión manual` al inicio del método | Priority 150; Phase 1 NO genera per-statement wrapping |
| `like_operator` | `s Like "A*"` | `System.Text.RegularExpressions.Regex.IsMatch(s, "^A")` con traducción wildcards: `*`→`.*`, `?`→`.`, `#`→`\d`, `[abc]`→`[abc]` | |
| `exponentiation` | `x ^ y` | `Math.Pow(x, y)` | |
| `cint_bool` | `CInt(True)` / `CInt(False)` | `(true ? -1 : 0)` / `0` | VB: True = -1; comentario `// VB: CInt(True) = -1` |
| `module_to_static` | `Module Foo` ... `End Module` | *Delegado a ICSharpCode* | Module es type-level — nodo `ModuleBlockSyntax` es top-level del archivo, nunca visitado por SeedRuleEngine method-level. ICSharpCode lo convierte a `static class`. Eliminado del catálogo (review v4). |
| `with_block` | `With obj` ... `End With` | *Delegado a ICSharpCode* | ICSharpCode cubre `With` blocks incluyendo anidados. No existe como SeedRule. Si ICSharpCode falla en edge case (With con expresión compleja), el método no tiene flag `WithBlock` en DifficultyMap → acceptance con confidence 0.85. Riesgo aceptado. |
| `string_concat` | `s1 & s2` | `s1 + s2` | Con `semanticModel`: si algún operando es tipo numérico, `&` sigue siendo concat en VB pero `+` en C# produce suma. Si semantic model confirma al menos un operando numérico → SUGGEST con confidence 0.70 en lugar de AUTO. Sin semantic model (`null`): si nombre del operando sugiere tipo numérico (heurística obvia), aplicar mismo downgrade a SUGGEST 0.70. |
| `integer_division` | `x \ y` | `(int)(x / y)` | |
| `is_nothing` | `x Is Nothing` | `x is null` | |
| `isnot_nothing` | `x IsNot Nothing` | `x is not null` | |
| `andalso` | `AndAlso` | `&&` | |
| `orelse` | `OrElse` | `\|\|` | |
| `byval_param` | `ByVal x As T` | `T x` | ByVal es default en C# |
| `optional_param` | `Optional ByVal x As T = v` | `T x = v` | |
| `redim_preserve` | `ReDim Preserve arr(n)` | `Array.Resize(ref arr, n + 1)` | n+1 porque ReDim es 0-based upper bound |
| `date_literal` | `#2020-01-01#` | `new DateTime(2020, 1, 1)` | regex para extraer partes |
| `erase_array` | `Erase arr` | `arr = null` | |
| `nothing_assign_valuetype` | `Dim x As Integer = Nothing` | `int x = default;` | `CanHandle` requiere `semanticModel != null` para confirmar que el tipo declarado es value type. Sin semantic model: `CanHandle` devuelve `false` → el nodo va a LLM. |
| `string_comparison_case` | `String.Compare(a, b, True)` | `string.Compare(a, b, StringComparison.OrdinalIgnoreCase)` | |
| `for_byte_overflow` | `For i As Byte = 0 To 255` | HumanQueue + flag `BYTE_LOOP_OVERFLOW` + comentario `// ⚠ VBMigrator: byte loop puede ser bucle infinito en VB` | **Limitación MVP:** detección por literal `255` en texto. Si el bound es constante (`Const MAX_BYTE = 255`), la regla no detecta el caso. Anotado como limitación conocida; Phase 2 usaría semantic model. |
| `imports_static` | `Imports System.Math` (con uso de `Sin()`, etc.) | *Delegado a ICSharpCode* | `ImportsStatementSyntax` es nodo top-level del archivo — SeedRuleEngine method-level nunca lo visita. ICSharpCode convierte `Imports System.Math` + bare `Sin()` → `System.Math.Sin()` fully qualified (semánticamente equivalente). Eliminado del catálogo (review v4). |
| `iif_function` | `IIf(condition, a, b)` | `(condition ? a : b)` | WARN **siempre** (independiente de side effects detectables): agregar `// ⚠ VBMigrator: IIf evalúa ambos brazos en VB; ternario ?: no lo hace` antes de la expresión. Sin excepciones. |
| `default_property` | *delegado a ICSharpCode.CodeConverter* | ICSharpCode maneja declaración e indexer; call sites requieren semantic model | No es SeedRule; es responsabilidad de RoslynTranslator |
| `root_namespace` | namespace implícito del `.vbproj` | inyectado por ProjectFileConverter como `<RootNamespace>` en `.csproj`; no se modifica el código | |

**Reglas delegadas a ICSharpCode.CodeConverter (no son SeedRules):**
- `using` / `Imports` simples (System.Collections.Generic, etc.)
- Propiedades auto-implementadas
- Lambdas y LINQ
- `default_property` (call sites)
- Herencia, interfaces
- `WithEvents` + `Handles` (transformación a nivel de clase — requiere contexto de campo + todos los métodos Handles; ISeedRule nodo-a-nodo no puede hacerlo. ICSharpCode lo cubre. Si falla, flag `WithEvents` en DifficultyMap enruta al LLM que tiene contexto suficiente.)
- `With` blocks / `with_block` (ICSharpCode cubre incluyendo anidados)

- `Module Foo` → `static class Foo` (`module_to_static`: nodo type-level, fuera del scope method-level de SeedRuleEngine)
- `Imports System.Math` → `System.Math.Sin()` fully qualified (`imports_static`: nodo file-level, ISeedRule no puede visitarlo)

**Nota:** `with_block`, `withevents_handler`, `module_to_static` e `imports_static` fueron removidos del catálogo de SeedRules (reviews v3-v4). Todos requieren contexto (clase, archivo, o estado) que ISeedRule method-level no puede proveer. ICSharpCode los cubre en el paso [2].

---

## 6. Learning Loop

### 6.1 Flujo de corrección (Phase 1 — sin embeddings)

```
Dev edita C# en Review Queue → "Editar y aprender"
    │
    ▼
CorrectionStore.SaveCorrectionAsync(vbInput, csCorrection, tag)
    │
    ├─→ PatternNormalizer:
    │      Roslyn parse el snippet VB
    │      Reemplaza identificadores con __var1__, __var2__, etc.
    │      Reemplaza tipos con __Type1__, __Type2__, etc.
    │      Output: vb_template normalizado
    │
    ├─→ Normaliza cs_correction:
    │      Variables con contrapartida en VB original → mismo __var1__, __var2__ (mismo mapping)
    │      Variables C#-introduced sin contrapartida VB (e.g., 'ex' en catch, '_w1') → __new1__, __new2__...
    │      Tipos → __Type1__, __Type2__ (mismo mapping que VB)
    │      Orden de aparición en el C# corregido determina el número serial
    │      Algoritmo determinista: dos correcciones equivalentes producen el mismo template
    │
    └─→ SQLite INSERT INTO patterns
           (tag, vb_template, cs_template, source='human', embedding=NULL)
           Si ya existe (tag + vb_template IGUALES post-normalización): UPDATE successes
           WHERE tag = @tag AND vb_template = @normalizedTemplate
           (NO usar LIKE — los templates tienen __var1__, __var2__ que LIKE no distingue)
```

**En Phase 1, el lookup del paso 4 del pipeline es:**
```sql
SELECT * FROM patterns
WHERE tag = @tag AND source = 'human'
ORDER BY successes DESC
LIMIT 1
```
Si hay resultado → inyectar como few-shot al LLM (no aplicar directamente — confianza aún no probada).

### 6.2 Schema SQLite

```sql
-- Migrations/001_initial.sql
CREATE TABLE IF NOT EXISTS patterns (
    id          TEXT PRIMARY KEY,
    tag         TEXT NOT NULL,
    vb_template TEXT NOT NULL,
    cs_template TEXT NOT NULL,
    embedding   BLOB,                   -- NULL en Phase 1
    source      TEXT NOT NULL DEFAULT 'seed',
    applied     INTEGER NOT NULL DEFAULT 0,
    successes   INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_patterns_tag ON patterns(tag);

CREATE TABLE IF NOT EXISTS translation_log (
    id              TEXT PRIMARY KEY,
    pattern_id      TEXT REFERENCES patterns(id),
    file_path       TEXT NOT NULL,
    method_name     TEXT,
    vb_input        TEXT NOT NULL,
    cs_output       TEXT NOT NULL,
    was_corrected   INTEGER NOT NULL DEFAULT 0,
    human_cs        TEXT,
    compiler_passed INTEGER NOT NULL DEFAULT 0,
    confidence      REAL NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL
);

CREATE VIEW IF NOT EXISTS pattern_stats AS
SELECT tag,
       COUNT(*) as total_patterns,
       SUM(applied) as total_applied,
       CAST(SUM(successes) AS REAL) / NULLIF(SUM(applied), 0) as success_rate
FROM patterns GROUP BY tag;
```

### 6.3 Confidence scoring

```
confidence = base_score
           + (applied > 0 ? (successes / applied) * 0.30 : 0)
clamp [0.0, 1.0]

base_score:
  'seed'          → 0.90
  'human'         → 0.70
  'verified_auto' → 0.80

Penalty: cada fallo en compile post-aplicación → -0.15 (actualizado en translation_log)
```

### 6.4 Embeddings — Phase 2

En Phase 1: `embedding = NULL`, lookup por tag exacto.
En Phase 2: CodeBERT ONNX → float[768], cosine similarity en memoria, thresholds 0.75 / 0.50.

---

## 7. ProjectFileConverter

Convierte `.vbproj` → `.csproj`. Necesario para que el output del CLI compile.

**Detección de formato:**
```
<Project Sdk="..."> presente → SDK-style (globs implícitos, pocos elements)
<Project> sin atributo Sdk   → old-style (elements explícitos: Compile, Import, Reference)
```

**Transformaciones old-style** (elements explícitos):
```
<Project Sdk="Microsoft.VisualBasic.App"> → <Project Sdk="Microsoft.NET.Sdk">
<TargetFramework>v4.8</TargetFramework>  → <TargetFramework>net48</TargetFramework>
<RootNamespace>MyApp</RootNamespace>     → preservar
<Compile Include="*.vb" />              → <Compile Include="*.cs" />
<Import Project="..." />               → eliminar imports VB-específicos
<Reference Include="Microsoft.VisualBasic"> → eliminar
```

**Transformaciones SDK-style** (globs implícitos — ItemGroups no existen):
```
<TargetFramework>vX.Y</TargetFramework>  → <TargetFramework>netXY</TargetFramework>
<RootNamespace>MyApp</RootNamespace>     → preservar
Sdk attribute                           → "Microsoft.VisualBasic.App" → "Microsoft.NET.Sdk"
(no Compile/Import/Reference elements — SDK-style los omite)
```

**COM references:** Preservar con comentario `<!-- VBMigrator: COM reference — verificar interop assembly -->`.
**NuGet PackageReferences:** Copiar sin cambios.
**Output:** Nuevo `.csproj` en directorio destino; `.vbproj` original no se modifica.

---

## 8. VS Extension (VSIX)

### 8.1 Arquitectura out-of-process

VSIX lanza CLI como proceso hijo. No referencia Core.

```csharp
// VSIX/Services/CliRunner.cs (net472)
var psi = new ProcessStartInfo
{
    FileName = CliLocator.FindExecutable(),   // orden: (1) Tools→VBMigrator→Settings path, (2) PATH del sistema, (3) error con mensaje de instalación
    Arguments = $"convert --file \"{filePath}\" --json-output",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
};
using var proc = Process.Start(psi)!;
// Drena stdout y stderr concurrentemente — evita deadlock si ICSharpCode emite
// warnings a stderr mientras stdout no se drena (OS buffer lleno → proceso bloqueado)
var stdoutTask = proc.StandardOutput.ReadToEndAsync();
var stderrTask  = proc.StandardError.ReadToEndAsync();
await proc.WaitForExitAsync();
var json   = await stdoutTask;
var _errors = await stderrTask;   // descartar o loggear; nunca ignorar la Task
var result = JsonSerializer.Deserialize<TranslationResultDto>(json);
```

`TranslationResultDto` es un DTO simple en VSIX (no referencia Models de Core). El CLI serializa `TranslationResult` a JSON cuando se pasa `--json-output`.

### 8.2 Puntos de entrada

```
Solution Explorer → right-click .vb file    → "Convert to C# (VBMigrator)"
Solution Explorer → right-click VB project  → "Convert Project to C# (VBMigrator)"
Solution Explorer → right-click Solution    → "Convert Solution to C# (VBMigrator)"
View → Other Windows                        → "VBMigrator Review Queue"
Tools → VBMigrator → Settings
  → Ruta al ejecutable vbmigrator
  → API key
  → Confidence thresholds
```

### 8.3 Review Queue Tool Window

```
┌─────────────────────────────────────────────────────┐
│ VBMigrator — Review Queue (12 pendientes)            │
├──────────────────────────┬──────────────────────────┤
│ MyForm.aspx.vb → .cs     │ ● REVISAR                │
│ Confidence: 0.61         │                          │
│ Tag: on_error_goto       │                          │
├──────────────────────────┴──────────────────────────┤
│ VB ORIGINAL              │ C# GENERADO              │
│ ──────────────────────── │ ──────────────────────── │
│ On Error GoTo ErrHandler │ try {                    │ ← read-only
│   x = Foo()              │     x = Foo();           │
│   Exit Sub               │ } catch (Exception ex) { │ ← editable
│ ErrHandler:              │     MessageBox.Show(     │   cuando se
│   MsgBox(Err.Description)│         ex.Message);     │   elige "Editar"
│                          │ }                        │
├──────────────────────────┴──────────────────────────┤
│ [✓ Aceptar]  [✎ Editar y aprender]  [✗ Manual]     │
└─────────────────────────────────────────────────────┘
```

"Editar y aprender" → dev edita el panel C# → guardar llama:
```
vbmigrator kb save --vb "..." --cs "..." --tag "on_error_goto"
```

---

## 9. CLI

### 9.1 Comandos

```bash
# Convertir archivo único (usado por VSIX ConvertFileCommand) — JSON por stdout
vbmigrator convert --file MyForm.vb [--json-output]

# Convertir solución completa (usado por VSIX ConvertSolutionCommand)
# NO usa --json-output: CLI escribe resultados a SQLite (translation_log).
# VSIX monitorea stderr para progreso y espera exit code.
# Al salir con código 0: VSIX abre Review Queue (lee desde DB).
# Formato de progreso en stderr: "FILE: MyForm.vb OK|FAIL|HUMAN_QUEUE"
vbmigrator convert --solution MyApp.sln --output ./migrated

# Dry-run: reporte HTML, no escribe archivos
vbmigrator convert --solution MyApp.sln --dry-run --report report.html

# Knowledge base
vbmigrator kb save --vb "..." --cs "..." --tag "on_error_goto"
vbmigrator kb stats

# Exit codes: 0 = ok, 1 = errores compilación en output, 2 = error proceso
```

### 9.2 JSON output mode (`--json-output`)

**Contrato de serialización:** el CLI configura `JsonSerializerOptions` con:
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` → propiedades emitidas en camelCase (`filePath`, `csSource`, etc.)
- `JsonStringEnumConverter` → enums como strings (`"Llm"`, `"HumanQueue"`), nunca enteros

`TranslationResultDto` en VSIX:
- Declara propiedades en camelCase **o** usa `[JsonPropertyName("filePath")]` para mapeo explícito
- Campos `route` y `llmFailureReason` declarados como `string` (no enum) para independencia de versiones

```json
{
  "filePath": "MyForm.vb",
  "csSource": "...",
  "confidence": 0.72,
  "route": "Llm",
  "compilerPassed": true,
  "compilerErrors": [],
  "patternTag": "on_error_goto",
  "llmFailureReason": null
}
```

---

## 10. Tech Stack

### 10.1 NuGet packages

| Proyecto | Paquete | Versión | Uso |
|---------|---------|---------|-----|
| Core | `Microsoft.CodeAnalysis.CSharp` | 4.* | Roslyn C# |
| Core | `Microsoft.CodeAnalysis.VisualBasic` | 4.* | Roslyn VB |
| Core | `Microsoft.CodeAnalysis.Workspaces.MSBuild` | 4.* | Cargar soluciones |
| Core | `ICSharpCode.CodeConverter` | 10.* | Base traducción (MIT, net6+) |
| Core | `Anthropic.SDK` | 3.* | Claude API (net6+) |
| Core | `Microsoft.Data.Sqlite` | 8.* | Correction DB |
| Core | `System.Text.Json` | 8.* | Serialización |
| CLI | `System.CommandLine` | 2.* | CLI parsing |
| CLI | `Microsoft.Build.Locator` | 1.* | MSBuild |
| VSIX | `Microsoft.VisualStudio.SDK` | 17.* | VS integration |
| VSIX | `Microsoft.VSSDK.BuildTools` | 17.* | VSIX build |
| Tests | `xunit` | 2.* | Test framework |
| Tests | `Microsoft.NET.Test.Sdk` | 17.* | Test runner |

**Embeddings (Phase 2):** `Microsoft.ML.OnnxRuntime` 1.19.* + `BERTTokenizers` 1.*

### 10.2 Targets

| Proyecto | Target |
|----------|--------|
| `VBMigrator.Core` | `net8.0` |
| `VBMigrator.CLI` | `net8.0` |
| `VBMigrator.VSIX` | `net472` |
| Tests | `net8.0` |

### 10.3 Decisiones técnicas

| Decisión | Elección | Razón |
|---------|---------|-------|
| Core target | net8.0 (no netstandard2.0) | Anthropic.SDK, ICSharpCode 10.x, OnnxRuntime requieren net6+ |
| VSIX integración | Out-of-process via CLI child process | Evita incompatibilidad net472 ↔ net8; CLI es la API boundary |
| Base traducción | ICSharpCode.CodeConverter (MIT) | Cubre 70%; agregar el 30% encima |
| LLM | Claude Sonnet 4.6 | Mejor en C# en benchmarks actuales |
| DB | SQLite + Microsoft.Data.Sqlite | Sin servidor, portable, net8 compatible |
| VSIX API | VSIX clásico (no VisualStudio.Extensibility) | Nueva API no soporta Tool Windows complejos en VS 2022 |
| ICSharpCode accuracy | Riesgo conocido aceptado | Métodos sin flags reciben confidence 0.85 basada en ICSharpCode. Bugs semánticos silenciosos de ICSharpCode pasan a AUTO sin detección — el Validator solo detecta errores de compilación. Mitigación: integration tests con output conocido de SampleVBProject. |

---

## 11. Configuración

```json
{
  "VBMigrator": {
    "LlmProvider": "Anthropic",
    "LlmModel": "claude-sonnet-4-6",
    "ApiKey": "",
    "AutoShipThreshold": 0.85,
    "SuggestThreshold": 0.65,
    "KnowledgeBasePath": "%APPDATA%\\VBMigrator\\kb.sqlite",
    "MaxParallelFiles": 4,
    "UseRepairAgent": true,
    "LlmRetryCount": 2,
    "LlmRetryBaseDelayMs": 1000
  }
}
```

Variables de entorno: `VBMIGRATOR_API_KEY`, `VBMIGRATOR_KB_PATH`.
En VSIX: ruta al ejecutable configurable en `Tools → VBMigrator → Settings`.

---

## 12. Estructura de Proyecto

```
VBMigrator/
├── src/
│   ├── VBMigrator.Core/
│   │   ├── Analyzer/
│   │   │   ├── DifficultyAnalyzer.cs
│   │   │   ├── DifficultyMap.cs
│   │   │   └── FlagDetectors/
│   │   │       ├── WithEventsDetector.cs
│   │   │       ├── OnErrorDetector.cs
│   │   │       ├── MyNamespaceDetector.cs
│   │   │       ├── LateBindingDetector.cs
│   │   │       └── OperatorDetector.cs
│   │   ├── Translator/
│   │   │   ├── TranslationPipeline.cs
│   │   │   ├── RoslynTranslator.cs      ← wrapper de ICSharpCode.CodeConverter.ConvertAsync; llamado por TranslationPipeline en paso [2] (whole-file VB→C# initial_cs); también aplica post-procesamiento Roslyn para default_property call sites que ICSharpCode no resuelve correctamente
│   │   │   ├── LlmTranslator.cs
│   │   │   ├── LlmUsingResolver.cs      ← tabla wellKnown tipo→namespace, ver §4.3
│   │   │   ├── RepairAgent.cs
│   │   │   ├── ConfidenceScorer.cs      ← agregación min(confidences) por método
│   │   │   └── Prompts/
│   │   │       ├── SystemPrompt.md          ← estructura mínima requerida: (1) rol: "Eres un traductor VB.NET→C# experto", (2) input: snippet VB delimitado, (3) output: solo el bloque C# equivalente sin imports adicionales ni using fuera del método, (4) constraints: no agregar tipos que no existen en el snippet, no refactorizar, preservar comentarios originales
│   │   │       └── RepairPrompt.md
│   │   ├── SeedRules/
│   │   │   ├── ISeedRule.cs
│   │   │   ├── SeedRuleEngine.cs
│   │   │   └── Rules/
│   │   │       ├── MyNamespaceRules.cs      ← 5 clases separadas en un archivo: MySettingsRule, MyFilesystemReadRule, MyFilesystemWriteRule, MyAppVersionRule, MyUserRule. Cada una con CanHandle/Convert propio. Sin dispatch interno.
│   │   │       // WithEventsRule.cs → eliminado (review v3). Delegado a ICSharpCode.
│   │   │       ├── OnErrorGotoRule.cs
│   │   │       ├── OnErrorResumeRule.cs     ← siempre HumanQueue + comentario WARN
│   │   │       ├── LikeOperatorRule.cs
│   │   │       ├── ExponentiationRule.cs
│   │   │       ├── CintBoolRule.cs          ← tag: cint_bool; CInt(True)→(true?-1:0)
│   │   │       // ModuleToStaticRule.cs → eliminado (review v4). Module es nodo type-level; delegado a ICSharpCode.
│   │   │       // WithBlockRewriter.cs → eliminado (review v3). Delegado a ICSharpCode. With blocks cubiertos en paso [2].
│   │   │       ├── StringConcatRule.cs
│   │   │       ├── IntegerDivisionRule.cs
│   │   │       ├── NullCheckRules.cs        ← is_nothing, isnot_nothing
│   │   │       ├── LogicalOperatorRules.cs  ← andalso, orelse
│   │   │       ├── ParameterRules.cs        ← byval_param, optional_param
│   │   │       ├── ArrayRules.cs            ← redim_preserve, erase_array
│   │   │       ├── DateLiteralRule.cs
│   │   │       ├── NothingValueTypeRule.cs
│   │   │       ├── StringComparisonRule.cs
│   │   │       ├── ByteLoopRule.cs          ← siempre HumanQueue + flag BYTE_LOOP_OVERFLOW
│   │   │       // ImportsStaticRule.cs → eliminado (review v4). ImportsStatementSyntax es file-level; delegado a ICSharpCode.
│   │   │       └── IifFunctionRule.cs
│   │   ├── AspxHandler/
│   │   │   ├── AspxDirectiveRewriter.cs
│   │   │   ├── EventWireupMigrator.cs
│   │   │   └── AspxMigrationResult.cs
│   │   │   // InlineVbExtractor.cs → Phase 2
│   │   ├── ProjectFileConverter/
│   │   │   └── VbprojToCsprojConverter.cs
│   │   ├── Validator/
│   │   │   ├── RoslynCompileValidator.cs
│   │   │   └── ValidationResult.cs
│   │   ├── Learning/
│   │   │   ├── CorrectionStore.cs
│   │   │   ├── PatternNormalizer.cs
│   │   │   └── Migrations/
│   │   │       └── 001_initial.sql
│   │   │   // EmbeddingService.cs → Phase 2
│   │   │   // SimilaritySearch.cs → Phase 2
│   │   └── Models/
│   │       ├── TranslationRequest.cs
│   │       ├── TranslationResult.cs
│   │       ├── TranslationRoute.cs
│   │       ├── LlmFailureReason.cs
│   │       ├── DifficultyMap.cs
│   │       ├── Pattern.cs
│   │       └── TranslationLog.cs
│   ├── VBMigrator.VSIX/
│   │   ├── source.extension.vsixmanifest
│   │   ├── VBMigratorPackage.cs
│   │   ├── Commands/
│   │   │   ├── ConvertFileCommand.cs
│   │   │   ├── ConvertProjectCommand.cs
│   │   │   └── ConvertSolutionCommand.cs
│   │   ├── ToolWindows/
│   │   │   ├── ReviewQueueWindow.cs
│   │   │   ├── ReviewQueueWindowControl.xaml
│   │   │   └── DiffViewerControl.xaml
│   │   └── Services/
│   │       ├── CliRunner.cs               ← lanza CLI como child process
│   │       ├── CliLocator.cs              ← busca ejecutable en PATH/config
│   │       └── TranslationResultDto.cs   ← DTO para deserializar JSON del CLI
│   └── VBMigrator.CLI/
│       ├── Program.cs
│       └── Commands/
│           ├── ConvertCommand.cs
│           ├── ReportCommand.cs
│           └── KbCommand.cs              ← kb save + kb stats (no export/import en MVP)
├── tests/
│   ├── VBMigrator.Core.Tests/
│   │   ├── SeedRules/                   ← 1 test por regla mínimo
│   │   ├── Analyzer/
│   │   ├── Translator/
│   │   ├── AspxHandler/
│   │   ├── ProjectFileConverter/
│   │   └── Learning/
│   └── VBMigrator.Integration.Tests/
│       └── EndToEnd/
└── samples/
    └── SampleVBProject/                 ← proyecto VB library/consola (sin WebForms)
        ├── SampleVBProject.vbproj        ← no referencia System.Web (incompatible net8.0)
        ├── SampleModule.vb               ← cubre: Module, On Error GoTo, With, My.Settings, etc.
        └── SampleClass.vb               ← cubre: WithEvents, Handles, IIf, operadores VB
    └── SampleAspxFiles/                 ← archivos .aspx mock para tests de AspxHandler
        ├── Default.aspx                  ← sin compilación: solo para parseo de directivas y Handles
        └── Default.aspx.vb
```

---

## 13. Fases de Implementación

### Phase 1 — MVP (scope de este documento)

**Incluido:**
- Core: todos los módulos salvo EmbeddingService, SimilaritySearch, InlineVbExtractor
- ProjectFileConverter
- CLI: convert + report + kb save + kb stats
- VSIX: right-click commands + ReviewQueue básico
- 25 SeedRules activas del catálogo (with_block, withevents_handler, module_to_static, imports_static removidas; my_filesystem_read/write cuenta como 2 — ver §5)

**Explícitamente fuera de scope MVP:**
- Embeddings / cosine similarity (EmbeddingService, SimilaritySearch)
- Inline VB en markup ASPX (`<% %>`)
- KB export/import JSON
- Dashboard de estadísticas avanzado
- on_error_resume per-statement wrapping (solo HumanQueue en MVP)
- Sistema de licencias

### Phase 2

- Embeddings ONNX (CodeBERT) + SimilaritySearch
- InlineVbExtractor para markup ASPX
- KB export/import
- UI polish VSIX (confidence highlighting mejorado)
- Sistema de licencias

---

## 14. Testing Strategy

- **Unit tests:** 1 test mínimo por SeedRule: input VB → output C# esperado
- **Integration tests:** traducción end-to-end de SampleVBProject (library/consola, sin System.Web); Roslyn compile check del output pasa
- **ASPX tests:** parseo de SampleAspxFiles/Default.aspx — solo verifican directivas reescritas y event subscriptions generadas; NO compilan output (evita dependencia System.Web incompatible con net8.0)
- **No mocks en Roslyn:** compilaciones reales en memoria — los mocks no detectan errores semánticos
- **VSIX:** smoke test manual; lógica de negocio testeable vía Core.Tests
- **Regression:** translation_log permite comparar output contra snapshots previos

---

## 15. Casos fuera de scope (explícito)

| Caso | Decisión |
|------|---------|
| VB6 (no VB.NET) | Fuera — lenguaje diferente |
| VBScript en ASP clásico | Fuera |
| COM-heavy sin interop assemblies | Flag HumanQueue; no auto-traducir |
| Fine-tuning LLM | Solo tras 500+ pares corregidos; roadmap futuro |
| VS Code extension | No en MVP |
| `on_error_resume` per-statement wrapping | Phase 2 |
| Inline VB en markup ASPX | Phase 2 |

---

*Spec revisado 2026-07-14 post-review v1–v6. Listo para plan de implementación.*

---

## Apéndice — Review v2: issues resueltos

| # | Severidad | Issue | Resuelto en |
|---|-----------|-------|-------------|
| 1 | 🔴 | ISeedRule string-based → SyntaxNode + SemanticModel? | §5 interfaz |
| 2 | 🔴 | with_block stateless → WithBlockRewriter pre-pass | §4.3, §5 tabla, §12 |
| 3 | 🔴 | ICSharpCode granularidad → Opción A whole-file definida | §4.1 pipeline |
| 4 | 🟠 | on_error_goto + GotoCrossBlock → HumanQueue CROSSBLOCK_GOTO | §5 tabla |
| 5 | 🟠 | iif_function WARN siempre con texto exacto | §5 tabla |
| 6 | 🟠 | string_concat tipo numérico → SUGGEST 0.70 | §5 tabla |
| 7 | 🟠 | CliLocator orden de búsqueda definido | §8.1 |
| 8 | 🟠 | JSON enum serialization → JsonStringEnumConverter | §9.2 |
| 9 | 🟠 | MyNamespaceRules.cs → 5 clases separadas | §12 |
| 10 | 🟡 | PatternNormalizer LIKE → igualdad exacta | §6.1 |
| 11 | 🟡 | SystemPrompt.md estructura mínima documentada | §12 |
| 12 | 🟡 | for_byte_overflow limitación textual anotada | §5 tabla |

## Apéndice — Review v3: issues resueltos

| # | Severidad | Issue | Resolución | Sección |
|---|-----------|-------|------------|---------|
| 1 | 🔴 | WithBlockRewriter : CSharpSyntaxRewriter incorrecto (With es VB, no C#) | Opción B: eliminado. With blocks delegados a ICSharpCode paso [2] | §4.1, §4.3, §5, §12 |
| 2 | 🔴 | withevents_handler requiere contexto de clase — ISeedRule nodo-a-nodo imposible | Eliminado del catálogo. Delegado a ICSharpCode. Flag WithEvents enruta a LLM si falla | §5, §12 |
| 3 | 🟠 | Matching VB↔C# falla en Sub New, Overloads, Operators | Política definida por caso + sin match → confidence 0.85 con riesgo documentado | §4.1 paso [4] |
| 4 | 🟠 | Agregación confidence por método no definida | min(confidences) — conservador, documenta rationale | §4.1 paso [9] |
| 5 | 🟠 | EventWireupMigrator input no definido | Consume VBSyntaxTree del Analyzer (sin re-parseo). Pipeline semi-paralelo documentado | §4.2 |
| 6 | 🟠 | LLM using statements — quién agrega los using | LlmUsingResolver: tabla wellKnown + Roslyn unresolved detection | §4.3, §12 |
| 7 | 🟠 | PatternNormalizer C# normalization algorithm no definido | VB-origin → __var1__; C#-introduced → __new1__; determinista | §6.1 |
| 8 | 🟠 | SampleVBProject WebForms + net8.0 — System.Web incompatible | SampleVBProject → library/consola. ASPX tests no compilan output | §12, §14 |
| 9 | 🟡 | ICSharpCode accuracy risk no documentado | Riesgo conocido aceptado en §10.3 | §10.3 |
| 10 | 🟡 | SeedRules redundantes no marcadas | with_block + withevents_handler removidas; count actualizado a 26 | §5, §13 |
| 11 | 🟡 | VbprojToCsprojConverter no detecta SDK-style | Detección formato + transformaciones por tipo documentadas | §7 |

## Apéndice — Review v4: issues resueltos

| # | Severidad | Issue | Resolución | Sección |
|---|-----------|-------|------------|---------|
| 1 | 🔴 | LlmUsingResolver corre entre SeedRules y LLM — no ve tipos del LLM | Reordenado: SeedRules → LLM → LlmUsingResolver → Validator | §4.1 paso [7] |
| 2 | 🟠 | imports_static y module_to_static son file/type-level — ISeedRule method-level no puede visitarlos | Opción A: ambos delegados a ICSharpCode. Eliminados del catálogo. Count → 25 | §5, §12, §13 |

## Apéndice — Review v5: issues resueltos

| # | Severidad | Issue | Resolución | Sección |
|---|-----------|-------|------------|---------|
| 1 | 🟠 | Count incorrecto (24 vs real 25: my_filesystem_read/write son 2 reglas) | 24 → 25 en §5, §4.1, §13 | §5, §4.1, §13 |
| 2 | 🟠 | CliRunner deadlock: ReadToEndAsync sin drena concurrente de stderr | Drena stdout + stderr en paralelo con WaitForExitAsync | §8.1 |
| 3 | 🟠 | JSON PropertyNamingPolicy no especificada — VSIX no sabe si esperar camelCase | CLI usa CamelCase + JsonStringEnumConverter. VSIX: camelCase o [JsonPropertyName] | §9.2 |

## Apéndice — Review v6: issues resueltos

| # | Severidad | Issue | Resolución | Sección |
|---|-----------|-------|------------|---------|
| 1 | 🟠 | imports_static citada en texto de interfaz §5 (ya eliminada v4) | Removida de la mención de semantic model | §5 |
| 2 | 🟠 | paso [6] dice "PatternNormalizer extrae tag" — incorrecto | Tag derivado desde DifficultyMap.Flags con mapeo flag→tag documentado | §4.1 |
| 3 | 🟠 | RoslynTranslator.cs sin responsabilidad en §12 | Responsabilidad documentada: wrapper ICSharpCode + default_property post-proc | §12 |
| 4 | 🟠 | ConvertSolutionCommand formato VSIX↔CLI no definido | Opción A: CLI→SQLite, VSIX lee exit code + stderr progress, abre Review Queue | §9.1 |
