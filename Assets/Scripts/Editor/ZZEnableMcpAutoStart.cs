using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// TEMPORARY. Turns on the MCP for Unity bridge's "Auto-Start on Editor Load"
    /// preference, then asks for one more domain reload so the package's own
    /// auto-start handler picks it up.
    ///
    /// Why a pref and not a direct call: MCPServiceLocator.Server.StartLocalHttpServer
    /// is public, but reaching it would mean adding an assembly reference from
    /// Cardio.Editor to MCPForUnity.Editor - coupling the project's build to a
    /// development tool, so removing the package later would break compilation.
    /// EditorPrefs is plain UnityEditor API and needs no reference at all.
    ///
    /// The bridge ships with auto-start OFF, which is why nothing was ever
    /// listening on the MCP port.
    ///
    /// DELETE THIS FILE once the bridge is up.
    /// </summary>
    [InitializeOnLoad]
    internal static class ZZEnableMcpAutoStart
    {
        private const string Pref = "MCPForUnity.AutoStartOnLoad";
        private const string ReloadedKey = "ZZEnableMcpAutoStart.RequestedReload";

        static ZZEnableMcpAutoStart()
        {
            if (Application.isBatchMode) return;

            bool already = EditorPrefs.GetBool(Pref, false);
            if (!already)
            {
                EditorPrefs.SetBool(Pref, true);
                Debug.Log($"[PSM2] Set {Pref} = true (was off, which is why the MCP port was closed).");
            }

            // The package reads that pref in its own [InitializeOnLoad] constructor,
            // and static constructor order within a reload is undefined - so setting
            // it now may well be too late for this pass. One extra reload removes the
            // race entirely. SessionState keeps this to a single retry per session,
            // so it cannot become a reload loop.
            if (SessionState.GetBool(ReloadedKey, false)) return;

            SessionState.SetBool(ReloadedKey, true);
            Debug.Log("[PSM2] Requesting one script reload so the MCP bridge picks up the preference.");

            EditorApplication.delayCall += () => EditorUtility.RequestScriptReload();
        }
    }
}
