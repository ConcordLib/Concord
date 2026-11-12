using System.Runtime.InteropServices;
using System.Text.Json;
using dnlib.DotNet;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class Measured {
    public const string InstallVariable = "CONCORD_GAME_INSTALL";

    public const string RuntimeDirKey = "Concord.ForkRuntimeDir";

    public static string Runtime => RuntimeInformation.FrameworkDescription;

    public static string Root {
        get {
            if (Environment.GetEnvironmentVariable(InstallVariable) is { Length: > 0 } overridden) {
                return overridden;
            }

            return AppContext.GetData(RuntimeDirKey) is string { Length: > 0 } configured
                ? configured
                : AppContext.BaseDirectory;
        }
    }

    public static string Require(string name) {
        string path = Path.Combine(Root, name);
        Assert.True(
            File.Exists(path),
            $"no '{name}' under '{Root}'. These are early-warning tests that read the WaylithCLR fork layout " +
            $"this process is running on, so they need one. Point {InstallVariable} at a fork directory, or " +
            $"build with -p:ForkRuntimeDir=<path>. This process reports runtime '{Runtime}'.");

        return path;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public class RuntimeBehaviourTests {
    private const string CoreLibFile = "System.Private.CoreLib.dll";

    private const string ConfigFile = "RimWorld.runtimeconfig.json";

    private const string HotReloadSwitch = "System.Reflection.Metadata.MetadataUpdater.IsSupported";

    private static readonly (string Behaviour, string Where, string Member)[] Surface = [
        (
            "field injection is off unless the host turns it on, so a batch must never be committed without probing first",
            "the game runtimeconfig sets WaylithClr.FieldInjection true; Commit throws NotSupportedException when it is false",
            "System.Boolean WaylithClr.FieldInjection::get_IsSupported()"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.FieldInjectionApplyResult::get_AllApplied()"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyUpdater::TryGetCurrentMethodBody(System.Reflection.MethodBase,System.Span`1<WaylithClr.MethodBodyClause>,WaylithClr.MethodBodyView&)"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyUpdater::TryGetCurrentMethodBody(System.RuntimeMethodHandle,System.Span`1<WaylithClr.MethodBodyClause>,WaylithClr.MethodBodyView&)"),
        (
            "reading a method body returns the ORIGINAL metadata IL, never the body currently live",
            "T14, and again as A17 finding 5",
            "System.Boolean WaylithClr.MethodBodyUpdater::TryGetDefaultMethodBody(System.Reflection.MethodBase,System.Span`1<WaylithClr.MethodBodyClause>,WaylithClr.MethodBodyView&)"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyUpdater::TryGetDefaultMethodBody(System.RuntimeMethodHandle,System.Span`1<WaylithClr.MethodBodyClause>,WaylithClr.MethodBodyView&)"),
        (
            "the runtime's own switch gates body replacement, which is what RuntimeCapability reads to stay off",
            "the probe verdict table, IsSupported=True with no env var set",
            "System.Boolean WaylithClr.MethodBodyUpdater::get_IsSupported()"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyView::get_InitLocals()"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyView::get_IsDefault()"),
        (
            "a body replaced in place is rejitted rather than poisoned, so the in-place path keeps working across a relayout and only DynamicMethods go stale",
            "AReplacedBodyKeepsRunningAfterItsOperandTypeIsRelaidOut, which saw 2 rejitted and 0 stale",
            "System.Collections.Generic.IReadOnlyList`1<System.Reflection.MethodBase> WaylithClr.FieldInjectionApplyResult::get_RejittedMethods()"),
        (string.Empty, string.Empty, "System.Collections.Generic.IReadOnlyList`1<System.Reflection.MethodBase> WaylithClr.FieldInjectionApplyResult::get_StaleDynamicMethods()"),
        (string.Empty, string.Empty, "System.Collections.Generic.IReadOnlyList`1<WaylithClr.FieldInjectionBatch> WaylithClr.FieldInjectionApplyResult::get_Applied()"),
        (string.Empty, string.Empty, "System.Collections.Generic.IReadOnlyList`1<WaylithClr.FieldInjectionBatch> WaylithClr.FieldInjectionApplyResult::get_Pending()"),
        (string.Empty, string.Empty, "System.Collections.Generic.IReadOnlyList`1<WaylithClr.InjectedField> WaylithClr.FieldInjectionBatch::get_Fields()"),
        (string.Empty, string.Empty, "System.Exception WaylithClr.FieldInjectionBatch::get_Error()"),
        (
            "the epoch moves on every relayout, which is the only cheap signal that a layout changed",
            "AReplacedBodyKeepsRunningAfterItsOperandTypeIsRelaidOut",
            "System.Int32 WaylithClr.FieldInjection::get_LayoutEpoch()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.FieldInjection::get_PendingCount()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.FieldInjectionBatchStatus::value__"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.FieldInjectionRefusal::value__"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.ILOffsetMapEntry::get_NewOffset()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.ILOffsetMapEntry::get_OriginalOffset()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.InjectedField::get_MetadataToken()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::get_ClassTokenOrFilterOffset()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::get_HandlerLength()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::get_HandlerOffset()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::get_TryLength()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::get_TryOffset()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Reflection.Emit.DynamicMethod)"),
        (
            "composed IL can name a field with one mint",
            "the api capture",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Reflection.FieldInfo)"),
        (
            "composed IL can name a method in another assembly with one mint, no trampoline",
            "T6",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Reflection.MethodBase)"),
        (
            "composed IL can name a string literal with one mint",
            "the api capture",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.String)"),
        (
            "composed IL can name a type with one mint",
            "the api capture",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Type)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.RuntimeMethodHandle,System.Reflection.Emit.DynamicMethod)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.RuntimeMethodHandle,System.Reflection.FieldInfo)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.RuntimeMethodHandle,System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.RuntimeMethodHandle,System.String)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.RuntimeMethodHandle,System.Type)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForCallSite(System.Reflection.Module,System.Type,System.ReadOnlySpan`1<System.Type>,System.Boolean)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForDefinition(System.Reflection.Module,System.Reflection.FieldInfo)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForDefinition(System.Reflection.Module,System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForInstantiation(System.Reflection.Module,System.Type)"),
        (
            "a fat-header body can carry real locals, minted as a standalone signature",
            "T5, GetTokenForSignature = 0x11000013",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForSignature(System.Reflection.Module,System.ReadOnlySpan`1<System.Byte>)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForSignature(System.RuntimeMethodHandle,System.ReadOnlySpan`1<System.Byte>)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForTypeSpec(System.Reflection.Module,System.ReadOnlySpan`1<System.Byte>)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForVarargCall(System.Reflection.Module,System.Reflection.MethodBase,System.Type[])"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForVarargCall(System.RuntimeMethodHandle,System.Reflection.MethodBase,System.Type[])"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyView::get_ClauseCount()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyView::get_LocalSignatureToken()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyView::get_MaxStackSize()"),
        (string.Empty, string.Empty, "System.Int64 WaylithClr.FieldInjectionApplyResult::get_MigratedInstances()"),
        (string.Empty, string.Empty, "System.Int64 WaylithClr.FieldInjectionBatch::get_MigratedInstances()"),
        (string.Empty, string.Empty, "System.Object WaylithClr.MethodBodyUpdater::ResolveToken(System.Reflection.MethodBase,System.Int32)"),
        (string.Empty, string.Empty, "System.Object WaylithClr.MethodBodyUpdater::ResolveToken(System.RuntimeMethodHandle,System.Int32)"),
        (string.Empty, string.Empty, "System.ReadOnlySpan`1<System.Byte> WaylithClr.MethodBodyView::get_IL()"),
        (string.Empty, string.Empty, "System.ReadOnlySpan`1<System.Byte> WaylithClr.MethodBodyView::get_LocalSignature()"),
        (string.Empty, string.Empty, "System.Reflection.ExceptionHandlingClauseOptions WaylithClr.MethodBodyClause::get_Flags()"),
        (string.Empty, string.Empty, "System.Reflection.FieldAttributes WaylithClr.InjectedField::get_Attributes()"),
        (
            "reflection sees an injected field once the batch applied, under the token InjectedField reports, so composed IL can name one",
            "ReflectionSeesAnInjectedFieldOnItsDeclaringType",
            "System.Reflection.FieldInfo WaylithClr.InjectedField::get_Field()"),
        (string.Empty, string.Empty, "System.Reflection.MethodBase WaylithClr.MethodBodyUpdater::GetMethodFromHandle(System.RuntimeMethodHandle)"),
        (string.Empty, string.Empty, "System.Reflection.MethodInfo WaylithClr.MethodBodyUpdater::CreateVariant(System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.RuntimeMethodHandle WaylithClr.MethodBodyUpdater::GetDefinitionHandle(System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.RuntimeMethodHandle WaylithClr.MethodBodyUpdater::GetMethodHandle(System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.RuntimeMethodHandle WaylithClr.MethodBodyUpdater::ResolveMethodDef(System.Reflection.Module,System.Int32)"),
        (
            "DependentFrames names the blocking frames, and the method that called apply is one of them when its own body touches a field of the target type",
            "AFrameBelowTheApplyThatReadsTheTargetTypeRefusesTheRelayout",
            "System.String WaylithClr.FieldInjectionBatch::get_PendingReason()"),
        (string.Empty, string.Empty, "System.String WaylithClr.InjectedField::get_Name()"),
        (string.Empty, string.Empty, "System.Type WaylithClr.InjectedField::get_DeclaringType()"),
        (string.Empty, string.Empty, "System.Type WaylithClr.InjectedField::get_FieldType()"),
        (string.Empty, string.Empty, "System.Void WaylithClr.FieldInjectionBatch::.ctor()"),
        (string.Empty, string.Empty, "System.Void WaylithClr.FieldInjectionBatch::Commit()"),
        (string.Empty, string.Empty, "System.Void WaylithClr.FieldInjectionBatch::OnMigrated(System.Type,System.Action`1<System.Object>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.FieldInjectionBatch::SetDefault(WaylithClr.InjectedField,System.Object)"),
        (
            "the offset map a stage publishes is a pair of ints, which is the shape the stage fills in",
            "the api capture",
            "System.Void WaylithClr.ILOffsetMapEntry::.ctor(System.Int32,System.Int32)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::PrepareGenericVariants(System.ReadOnlySpan`1<System.RuntimeMethodHandle>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::PrepareGenericVariants(System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::PrepareGenericVariants(System.RuntimeMethodHandle)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::PrepareMethodBodies(System.ReadOnlySpan`1<System.RuntimeMethodHandle>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::PrepareMethodBody(System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::PrepareMethodBody(System.RuntimeMethodHandle)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBodies(System.ReadOnlySpan`1<System.RuntimeMethodHandle>,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<System.Int32>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>,System.ReadOnlySpan`1<System.Int32>,System.ReadOnlySpan`1<System.Object>)"),
        (
            "publishing a body is last-write-wins, with no chaining",
            "T7, and the 200-cycle rerun in P1T2c",
            "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBody(System.Reflection.MethodBase,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>,System.Object)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBody(System.RuntimeMethodHandle,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>,System.Object)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::Revert(System.ReadOnlySpan`1<System.RuntimeMethodHandle>)"),
        (
            "revert restores the ORIGINAL body, not the previous one, so rollback has to re-publish",
            "T8, and P1T2c",
            "System.Void WaylithClr.MethodBodyUpdater::Revert(System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::Revert(System.RuntimeMethodHandle)"),
        (string.Empty, string.Empty, "System.Void modreq(System.Runtime.CompilerServices.IsExternalInit) WaylithClr.MethodBodyClause::set_ClassTokenOrFilterOffset(System.Int32)"),
        (string.Empty, string.Empty, "System.Void modreq(System.Runtime.CompilerServices.IsExternalInit) WaylithClr.MethodBodyClause::set_Flags(System.Reflection.ExceptionHandlingClauseOptions)"),
        (string.Empty, string.Empty, "System.Void modreq(System.Runtime.CompilerServices.IsExternalInit) WaylithClr.MethodBodyClause::set_HandlerLength(System.Int32)"),
        (string.Empty, string.Empty, "System.Void modreq(System.Runtime.CompilerServices.IsExternalInit) WaylithClr.MethodBodyClause::set_HandlerOffset(System.Int32)"),
        (string.Empty, string.Empty, "System.Void modreq(System.Runtime.CompilerServices.IsExternalInit) WaylithClr.MethodBodyClause::set_TryLength(System.Int32)"),
        (string.Empty, string.Empty, "System.Void modreq(System.Runtime.CompilerServices.IsExternalInit) WaylithClr.MethodBodyClause::set_TryOffset(System.Int32)"),
        (
            "an apply drains every queued batch, so one consumer calling it applies another consumer's batches too",
            "FieldInjectionTests, and FieldInjection.ApplyPending over the shared s_queue",
            "WaylithClr.FieldInjectionApplyResult WaylithClr.FieldInjection::ApplyPending()"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionApplyResult WaylithClr.FieldInjectionBatch::CommitAndApply()"),
        (
            "an apply has three verdicts, and Failed dequeues the batch while a refusal keeps it queued for the host to retry",
            "FieldInjectionTests, and FieldInjection.ApplyPending's catch path",
            "WaylithClr.FieldInjectionBatchStatus WaylithClr.FieldInjectionBatch::get_Status()"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionBatchStatus WaylithClr.FieldInjectionBatchStatus::Applied"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionBatchStatus WaylithClr.FieldInjectionBatchStatus::Building"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionBatchStatus WaylithClr.FieldInjectionBatchStatus::Failed"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionBatchStatus WaylithClr.FieldInjectionBatchStatus::Queued"),
        (
            "the refusal reported is whichever gate blocks first, so CompileGateBusy masks DependentFrames while anything else publishes a body",
            "AFrameBelowTheApplyThatReadsTheTargetTypeRefusesTheRelayout, which only reports DependentFrames after retrying past the transient gates",
            "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionBatch::get_LastRefusal()"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::CompileGateBusy"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::DependentFrames"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::GCBusy"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::GCNotLatched"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::NoGCRegions"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::None"),
        (string.Empty, string.Empty, "WaylithClr.FieldInjectionRefusal WaylithClr.FieldInjectionRefusal::PinnedInstance"),
        (
            "a field can only be added to an assembly named in WaylithClr.JitPatchableAssemblies, and any other module fails as a framework assembly",
            "AnAssemblyOutsideJitPatchableAssembliesIsRefusedAsAFrameworkAssembly",
            "WaylithClr.InjectedField WaylithClr.FieldInjectionBatch::AddField(System.Type,System.String,System.Type,System.Reflection.FieldAttributes)"),
        (string.Empty, string.Empty, "WaylithClr.MethodBodyFlags WaylithClr.MethodBodyView::get_Flags()"),
    ];

    private static readonly string[] MeasuredTypes = [
        "WaylithClr.MethodBodyUpdater",
        "WaylithClr.MethodBodyView",
        "WaylithClr.MethodBodyClause",
        "WaylithClr.ILOffsetMapEntry",
        "WaylithClr.FieldInjection",
        "WaylithClr.FieldInjectionBatch",
        "WaylithClr.FieldInjectionApplyResult",
        "WaylithClr.FieldInjectionBatchStatus",
        "WaylithClr.FieldInjectionRefusal",
        "WaylithClr.InjectedField",
    ];

    public static TheoryData<string, string, string> LoadBearing {
        get {
            TheoryData<string, string, string> rows = [];
            foreach ((string behaviour, string where, string member) in Surface) {
                if (behaviour.Length > 0) {
                    rows.Add(behaviour, where, member);
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(LoadBearing))]
    public void TheEntryPointAMeasuredBehaviourWasObservedThroughIsStillOnTheShippedFork(
        string behaviour,
        string where,
        string member) {
        HashSet<string> shipped = PublicSurface(Measured.Require(CoreLibFile));

        Assert.True(
            shipped.Contains(member),
            $"the measured behaviour '{behaviour}' no longer has an entry point on the shipped fork. " +
            $"'{member}' is gone or changed signature, so the measurement it was taken through does not " +
            $"describe this runtime any more. It was measured as {where}, on runtime '{Measured.Runtime}'. " +
            "The adapter binds this api reflectively and falls back to the detour backend when a bind fails, " +
            "so a rename downgrades the whole backend silently instead of failing. Shipped surface under " +
            $"'{Measured.Root}':" +
            Listing(shipped.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void TheShippedForkExposesExactlyTheApiSurfaceTheProbesMeasured() {
        HashSet<string> shipped = PublicSurface(Measured.Require(CoreLibFile));
        HashSet<string> expected = [.. Surface.Select(entry => entry.Member)];

        List<string> gone = [.. expected.Except(shipped).Order(StringComparer.Ordinal)];
        List<string> added = [.. shipped.Except(expected).Order(StringComparer.Ordinal)];

        Assert.True(
            gone.Count == 0 && added.Count == 0,
            "the WaylithCLR body-replacement api changed shape under us. Every behaviour below was measured " +
            $"against the surface captured here, on runtime '{Measured.Runtime}'. An addition matters as much " +
            "as a removal: a Revert overload naming a version or a step would mean chaining arrived, and T7's " +
            "last-write-wins result would stop being the whole story. Decide what the change means before " +
            "trusting any measurement in this file." +
            $"\n  gone ({gone.Count}):" + Listing(gone) +
            $"\n  added ({added.Count}):" + Listing(added));
    }

    [Fact]
    public void TheAdapterStillNamesTheTypeTheShippedForkActuallyDeclares() {
        string[] parts = RuntimeCapability.UpdaterTypeName.Split(',', 2, StringSplitOptions.TrimEntries);
        ModuleDefMD corelib = ModuleDefMD.Load(Measured.Require(CoreLibFile));

        Assert.True(
            parts.Length == 2 && parts[1] == corelib.Assembly.Name,
            $"'{nameof(RuntimeCapability)}.{nameof(RuntimeCapability.UpdaterTypeName)}' is " +
            $"'{RuntimeCapability.UpdaterTypeName}', which does not name assembly '{corelib.Assembly.Name}' as " +
            $"shipped under '{Measured.Root}'. The adapter resolves that string with Type.GetType and reports " +
            "ApiAbsent when it misses, so a mismatch turns the waylith backend off with no error anywhere.");

        Assert.True(
            corelib.Find(parts[0], isReflectionName: false) is not null,
            $"'{parts[0]}' is not declared by '{corelib.Assembly.Name}' as shipped under '{Measured.Root}', but " +
            $"'{nameof(RuntimeCapability)}.{nameof(RuntimeCapability.UpdaterTypeName)}' still names it. Every " +
            "behaviour in this file was measured through that type. The adapter would report ApiAbsent and fall " +
            "back to detours without saying anything was wrong.");
    }

    [Fact]
    public void TheForkLayoutBeingReadIsTheRuntimeThisProcessRunsOn() {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Measured.Require(ConfigFile)));
        JsonElement frameworks = config.RootElement.GetProperty("runtimeOptions").GetProperty("includedFrameworks");
        string? installed = frameworks[0].GetProperty("version").GetString();

        Assert.True(
            installed is { Length: > 0 } && Measured.Runtime.Contains(installed, StringComparison.Ordinal),
            $"'{ConfigFile}' under '{Measured.Root}' carries '{installed}' and this process reports " +
            $"'{Measured.Runtime}', so every constant in this file was read off one fork build and checked " +
            "against a different one. The api surface, the member names and the hot-reload switch all come " +
            $"from '{Measured.Root}'. Point {Measured.InstallVariable} at the directory this process is " +
            "actually running on, or rebuild with -p:ForkRuntimeDir pointing at it.");
    }

    [Fact]
    public void HotReloadIsStillOffSoThePipelineIsTheOnlyWriterOfBodies() {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Measured.Require(ConfigFile)));
        JsonElement properties = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(
            properties.TryGetProperty(HotReloadSwitch, out JsonElement reload),
            $"'{HotReloadSwitch}' is no longer set in '{ConfigFile}' under '{Measured.Root}'. With it unset the " +
            "stock metadata updater can install a body of its own, outside the transformer pipeline, and the " +
            "effective value decides whether that can happen here.");

        Assert.True(
            reload.ValueKind == JsonValueKind.False,
            $"'{HotReloadSwitch}' is now '{reload}' in '{ConfigFile}' under '{Measured.Root}', not false. With " +
            "the stock metadata updater on, a second writer can replace a method body without going through " +
            "the transformer pipeline. Concord's stage composes against the body the pipeline hands it, so a " +
            "body installed behind the pipeline is one the stage never sees and never composes over.");
    }

    private static HashSet<string> PublicSurface(string corelib) {
        ModuleDefMD module = ModuleDefMD.Load(corelib);
        HashSet<string> surface = [];
        foreach (string name in MeasuredTypes) {
            if (module.Find(name, isReflectionName: false) is not { } type) {
                continue;
            }

            foreach (MethodDef method in type.Methods) {
                if (method.IsPublic) {
                    surface.Add(method.FullName);
                }
            }

            foreach (FieldDef field in type.Fields) {
                if (field.IsPublic) {
                    surface.Add(field.FullName);
                }
            }
        }

        return surface;
    }

    private static string Listing(IEnumerable<string> entries) {
        return string.Concat(entries.Select(entry => "\n    " + entry));
    }
}
