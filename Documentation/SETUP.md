# Setup and build guide

## 1. Open the project

The repository already contains a valid Unity project layout:

```
PSM 2 Along/
├── Assets/            <- all source and generated content
├── Packages/          <- manifest.json declares the required packages
├── ProjectSettings/   <- contains ProjectVersion.txt
└── Documentation/
```

1. Install Unity **6000.5 LTS** through Unity Hub (6000.0 LTS also works).
2. Unity Hub ▸ **Add** ▸ **Add project from disk** ▸ select this folder.
3. If Hub reports that the required editor version is missing, either install a
   matching editor or open `ProjectSettings/ProjectVersion.txt` and change the version
   string to one you already have. Unity will offer to upgrade the project; accept.

> The project uses the **Built-in Render Pipeline**. If you create the project from a
> URP template instead, materials still resolve — `ProjectAssets.FindLitShader()`
> falls back from `Standard` to `Universal Render Pipeline/Lit`.

### Required packages — do not delete `Packages/manifest.json`

`Packages/manifest.json` **must** be committed with the project.

If it is missing, Unity does *not* reconstruct a working set. It falls back to
`resetToDefaultDependencies`, which writes only the `com.unity.modules.*` engine
modules plus `com.unity.multiplayer.center`. **`com.unity.ugui` is not in that
fallback set**, so `UnityEngine.UI` and `TMPro` vanish and every UI script fails to
compile — roughly 195 `CS0246`/`CS0234` errors across the nine files that touch the
interface. This is not version-specific; it happens on any editor version.

| Package | Version | Why it is needed |
|---|---|---|
| `com.unity.ugui` | 2.5.0 | **Required.** Supplies `UnityEngine.UI` (Button, Image, Slider, Toggle, Canvas components) *and* `TMPro`. Every script under `Assets/Scripts/UI` plus `AnatomyMarker` depends on it. |
| `com.unity.test-framework` | 1.7.0 | Required for the Phase 10 white-box tests. Pulls in `com.unity.ext.nunit`. |
| `com.unity.ide.visualstudio` | 2.0.27 | IDE integration — IntelliSense and debugger attach. Serves both Visual Studio and **VS Code** (via Microsoft's Unity extension). Swap for `com.unity.ide.rider` if you use Rider. Not needed to build. |

`com.unity.ugui` and `com.unity.test-framework` ship inside the editor at
`Editor\Data\Resources\PackageManager\BuiltInPackages`, so they resolve **offline**.
`com.unity.ide.visualstudio` is a registry package and needs internet on first resolve;
it is the only optional entry, so remove that line if you must work fully offline.

> **Never add `com.unity.textmeshpro` alongside `com.unity.ugui`.** As of Unity 6, TMP
> is merged into uGUI 2.x and the standalone package is a legacy shim. Declaring both
> can produce duplicate-assembly errors.

## 2. Generate the content

The scenes, prefabs and materials are produced by an editor tool rather than being
committed as binary `.unity` files. This keeps the project reviewable as source and
avoids the merge conflicts that Unity scene files normally cause.

Run:

```
PSM2 ▸ Setup ▸ Build or Rebuild Project
```

This performs, in order:

| Step | Produces |
|---|---|
| Folder structure | `Assets/Scripts/**`, `Prefabs`, `Materials`, `Scenes`, `Data`, … |
| Tags and layers | `Hazard`, `Interactable`; layers `Environment`, `Obstacle`, `PlayerLayer` |
| Materials | 12 flat voxel materials in `Assets/Materials` |
| Player prefab | `Assets/Prefabs/Player/Player_BlooDClot.prefab` |
| Scenes | MainMenu, Login, Level1_LeftVentricle, Level2_Brain, Level3_RightVentricle |
| Build Settings | the five scenes registered in play order |
| Player/Quality settings | product name, 1600×900 default, vSync on, short shadow distance |

Re-running it is safe. **It replaces the generated scenes**, so if you hand-edit a
scene, either stop running the tool or fold your change back into `SceneFactory.cs`.

### If TextMeshPro asks for resources

On a fresh project TMP needs its Essential Resources imported before any text can
render. The tool detects this and offers to import them. Accept, wait for the
reimport to finish, then **run the tool again** — it stops on purpose after the
import because Unity reloads the domain.

You can also do it manually: `Window ▸ TextMeshPro ▸ Import TMP Essential Resources`.

## 3. Play

Two ways to test:

- Open `Assets/Scenes/MainMenu.unity` and press Play for the full flow.
- Open any level scene and press Play to go straight to gameplay.

The second works because `GameBootstrap` creates the persistent systems with
`[RuntimeInitializeOnLoadMethod]` before the first scene loads — no manager needs to
be dragged into a scene, and no scene depends on having been entered from the menu.

## 4. Build a Windows executable

1. `File ▸ Build Settings`
2. Platform: **Windows, Mac, Linux** → Target Platform **Windows**, Architecture **x86_64**
3. Confirm the scene list shows all five scenes with MainMenu at index 0
   (or re-run `PSM2 ▸ Setup ▸ Register Scenes in Build Settings`)
4. **Build** → choose an output folder outside `Assets/` (e.g. `Builds/`)

`.gitignore` already excludes `Builds/`, `Library/` and `Temp/`.

## 5. Where things are stored at runtime

| Data | Location |
|---|---|
| Progress and unlocked levels | `%USERPROFILE%\AppData\LocalLow\PSM2 FYP\Cardiovascular Exploration\psm2_progress.json` |
| Options (volume, sensitivity, fullscreen) | Windows registry, via `PlayerPrefs` |

Shortcut: `PSM2 ▸ Open Save File Folder`.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `CS0234: 'UI' does not exist in the namespace 'UnityEngine'` and/or `CS0246: 'TMPro' could not be found` | `com.unity.ugui` is missing from `Packages/manifest.json` — see "Required packages" above. The `PSM2` menu will also be absent, because the editor assembly cannot compile |
| The `PSM2` menu is missing from the menu bar | `Assembly-CSharp-Editor` failed to compile. Fix the Console errors first; the menu is registered by `PSM2SetupTool.cs` |
| "Scene 'X' is not in Build Settings" in the Console | Run `PSM2 ▸ Setup ▸ Register Scenes in Build Settings` |
| All UI text is invisible | TMP Essential Resources missing — see step 2 |
| Everything renders magenta | Render pipeline mismatch; re-run the setup tool so materials are recreated against the active pipeline |
| Player falls through the floor | The environment was deleted or regenerated while Play mode was running; exit Play mode and re-run the setup tool |
| Camera does not rotate | Another window has focus, or `Esc` left the cursor unlocked — click inside the Game view |
