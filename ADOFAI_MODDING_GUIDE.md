# ADOFAI 모드 개발 메모 — Euclid에서 정리한 실전 구조

이 문서는 Euclid를 ADOFAI 3.3.0에서 실제로 만들면서 확인한 구조를 바탕으로, 다음 얼불춤 모드를 시작할 때 다시 역분석부터 하지 않도록 정리한 메모입니다.

기준 환경:

- A Dance of Fire and Ice 3.3.0
- Unity 6000.3.10f1
- Unity Mod Manager(UMM)
- C# / .NET Framework 4.8 (`net48`)
- Windows Steam 설치본 기준

중요: ADOFAI의 `Assembly-CSharp.dll` 내부 클래스는 정식 모드 API가 아닙니다. 버전이 바뀌면 클래스/필드/메서드 이름과 UI 계층이 달라질 수 있습니다. 따라서 아래의 ADOFAI 내부 API 부분은 “3.3.0에서 Euclid가 이렇게 접근했다”는 참고 자료로 봐야 합니다.

---

## 1. 가장 먼저 알아둘 구조

UMM 모드는 소스 코드를 읽어서 실행하는 방식이 아닙니다. 미리 컴파일한 DLL과 `Info.json`을 UMM이 읽습니다.

최종 배포 ZIP은 보통 다음처럼 만듭니다.

```text
MyMod-1.0.0.zip
└─ MyMod/
   ├─ Info.json
   └─ MyMod.dll
```

Euclid도 같은 구조입니다.

```text
Euclid-0.7.62.zip
└─ Euclid/
   ├─ Info.json
   └─ Euclid.dll
```

최소 `Info.json` 예시:

```json
{
  "Id": "MyMod",
  "DisplayName": "My Mod",
  "Author": "YourName",
  "Version": "1.0.0",
  "ManagerVersion": "0.22.14.0",
  "AssemblyName": "MyMod.dll",
  "EntryMethod": "MyMod.Startup.Load"
}
```

최소 진입점:

```csharp
using UnityModManagerNet;

namespace MyMod
{
    internal static class Startup
    {
        internal static void Load(UnityModManager.ModEntry modEntry)
        {
            MainMod.Load(modEntry);
        }
    }
}
```

그리고 실제 초기화는 별도 클래스에 둡니다.

```csharp
using UnityEngine;
using UnityModManagerNet;

namespace MyMod
{
    internal static class MainMod
    {
        internal static bool Enabled { get; private set; }
        internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; }

        internal static void Load(UnityModManager.ModEntry entry)
        {
            Logger = entry.Logger;
            entry.OnToggle = OnToggle;

            var obj = new GameObject("MyMod");
            Object.DontDestroyOnLoad(obj);
            obj.AddComponent<MyModBehaviour>();
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            Enabled = value;
            return true;
        }
    }
}
```

Euclid에서는 이 역할이 각각 다음 파일입니다.

```text
Startup.cs
EuclidMod.cs
EuclidBehaviour.cs
```

### 왜 진입점을 얇게 두는가

`Startup.Load`에 모든 코드를 몰아넣으면 나중에 UMM 초기화, 설정, Unity Update, 에디터 UI, 게임 데이터 접근이 섞입니다. 실제 기능은 `Mod`, `Behaviour`, 기능별 서비스/도구 클래스로 나눠두는 것이 수정하기 훨씬 쉽습니다.

---

## 2. 새 프로젝트를 만들 때 추천 파일 구조

간단한 모드라면 아래 정도면 충분합니다.

```text
MyMod/
├─ MyMod.csproj
├─ Info.json
├─ Startup.cs
├─ MyMod.cs
├─ MyModBehaviour.cs
├─ GameCompat.cs
├─ README.md
└─ BUILD_RELEASE.cmd          # 선택
```

에디터 기능이 커지면 Euclid처럼 분리하는 편이 좋습니다.

```text
MyMod/
├─ Startup.cs                # UMM entry
├─ MyMod.cs                  # UMM callbacks/settings/bootstrap
├─ MyModBehaviour.cs         # Update/OnGUI runtime coordination
│
├─ Editor/
│  ├─ MyPanel.cs             # panel/tab lifecycle
│  ├─ MyPanel.UiFactory.cs   # Unity UI construction
│  ├─ MyPanel.Style.cs       # native style capture
│  └─ MyPanel.Interaction.cs # game editor synchronization
│
├─ Features/
│  ├─ FeatureState.cs        # pure-ish state/model
│  ├─ FeatureGeometry.cs     # calculations
│  ├─ FeatureOverlay.cs      # rendering
│  └─ FeatureEditor.cs       # mutation of ADOFAI data
│
├─ Compat/
│  ├─ GameCompat.cs
│  └─ LevelEventCompat.cs
│
├─ Localization/
│  ├─ en.lang
│  └─ ko.lang
│
└─ scripts/
   └─ check_project.ps1
```

처음부터 폴더까지 반드시 이렇게 나눌 필요는 없습니다. 중요한 것은 역할 경계를 유지하는 것입니다.

권장 경계:

```text
UMM/부트스트랩
      ↓
런타임 조정(Update/Input)
      ↓
기능 상태·계산 ──→ 렌더러
      ↓
ADOFAI 데이터 변경
      ↓
호환성 계층(GameCompat)
```

---

## 3. `.csproj`에서 가장 중요한 부분

ADOFAI 설치 경로에서 직접 DLL을 참조하면 게임 버전과 맞는 어셈블리에 바로 컴파일할 수 있습니다.

Euclid 방식의 핵심 구조:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <OutputType>Library</OutputType>
    <AssemblyName>MyMod</AssemblyName>
    <RootNamespace>MyMod</RootNamespace>
  </PropertyGroup>

  <PropertyGroup>
    <GameDir Condition="'$(GameDir)' == '' and '$(ADOFAI_DIR)' != ''">$(ADOFAI_DIR)</GameDir>
    <GameDir Condition="'$(GameDir)' == ''">C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice</GameDir>
    <ManagedAssembliesDir>$(GameDir)\A Dance of Fire and Ice_Data\Managed</ManagedAssembliesDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ManagedAssembliesDir)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>

    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ManagedAssembliesDir)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>

    <Reference Include="UnityModManager">
      <HintPath>$(ManagedAssembliesDir)\UnityModManager\UnityModManager.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

실제로 필요한 Unity DLL은 기능에 따라 추가합니다. Euclid는 UI/TMP/Input을 쓰기 때문에 다음도 참조합니다.

```text
UnityEngine.dll
UnityEngine.CoreModule.dll
UnityEngine.IMGUIModule.dll
UnityEngine.InputLegacyModule.dll
UnityEngine.TextRenderingModule.dll
UnityEngine.TextCoreFontEngineModule.dll
UnityEngine.TextCoreTextEngineModule.dll
UnityEngine.UIModule.dll
UnityEngine.UI.dll
Unity.TextMeshPro.dll
netstandard.dll
RDTools.dll
```

### `<Private>false>`를 쓰는 이유

이 DLL들은 게임에 이미 있습니다. 빌드 결과에 Unity/게임 DLL 복사본이 섞이지 않게 하는 것이 좋습니다. 모드 ZIP에는 기본적으로 내 DLL과 모드가 직접 배포해야 하는 리소스만 넣습니다.

### 게임 설치 위치가 다를 때

프로젝트 파일을 매번 수정하지 말고 속성으로 넘깁니다.

```bat
dotnet build MyMod.csproj -c Release "-p:GameDir=D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice"
```

환경 변수 `ADOFAI_DIR`을 쓰는 방식도 편합니다.

---

## 4. 무엇을 역컴파일해서 봐야 하는가

ADOFAI 내부 동작을 확인할 때 가장 먼저 보는 파일은 다음입니다.

```text
A Dance of Fire and Ice_Data\Managed\Assembly-CSharp.dll
```

Euclid에서 자주 확인한 타입 예시:

```text
scnEditor
InspectorPanel
scrFloor
ADOFAI.LevelEvent
LevelEventType
RDString
```

기능을 만들 때는 “비슷해 보이는 클래스 이름”을 추측하지 말고 실제 현재 게임 DLL을 검색하는 편이 안전합니다.

예를 들어 에디터 이벤트를 수정하고 싶다면 다음 순서로 봅니다.

1. `scnEditor`에서 현재 선택/패널/저장 상태 관련 필드를 찾습니다.
2. `LevelEvent`의 데이터 저장 형식을 확인합니다.
3. 기본 에디터가 해당 값을 바꿀 때 어떤 메서드를 호출하는지 역추적합니다.
4. Undo 저장, 실제 이벤트 적용, unsaved 표시, inspector refresh가 어떻게 이어지는지 확인합니다.
5. 필요한 최소 호출만 모드 쪽 호환성 계층으로 가져옵니다.

### 역컴파일 도구

현재 쓸 만한 오픈소스 .NET 역컴파일러:

- ILSpy: https://github.com/icsharpcode/ILSpy

검색할 때는 타입 이름뿐 아니라 UI에 실제 보이는 문자열, 이벤트 키(`position`, `relativeTo` 등), 필드 이름도 같이 검색하면 빨리 찾을 수 있습니다.

---

## 5. `GameCompat.cs`를 처음부터 만드는 것을 권장

게임 내부 멤버에 직접 접근하는 코드가 여러 파일로 퍼지면 버전 업데이트 때 전부 찾아다녀야 합니다.

나쁜 예:

```csharp
// PanelA.cs
var field = typeof(scnEditor).GetField("someField", flags);

// FeatureB.cs
var panel = typeof(scnEditor).GetProperty("somePanel", flags);

// FeatureC.cs
editor.GetType().GetMethod("SaveState", flags)?.Invoke(editor, null);
```

권장:

```csharp
internal static class GameCompat
{
    internal static bool TrySaveState(scnEditor editor)
    {
        // direct call 또는 reflection fallback
    }

    internal static object GetEventPanel(scnEditor editor)
    {
        // 버전별 이름 차이를 여기서 처리
    }
}
```

그러면 기능 코드는 다음처럼 읽힙니다.

```csharp
GameCompat.TrySaveState(editor);
GameCompat.TryApplyPropertiesToRealEvents(ev);
GameCompat.TryRefreshEventPanel(editor, ev);
```

Euclid에서 버전 민감한 코드를 볼 때 우선 참고할 파일:

```text
GameCompat.cs
LevelEventCompat.cs
CameraFrameEditor.cs
```

---

## 6. `LevelEvent`를 수정할 때 단순 대입만 하면 부족할 수 있음

에디터 이벤트 값은 값만 바뀌면 끝이 아닙니다. 에디터의 Undo, 상속/disabled 상태, 실제 재생용 이벤트, inspector 표시, 저장 dirty 상태도 같이 맞아야 합니다.

Euclid의 `CameraFrameEditor.TrySetVectorProperty`가 사용하는 흐름은 다음입니다.

```text
scnEditor.instance
   ↓
SaveState (Undo가 필요한 변경일 때)
   ↓
LevelEventCompat.SetRaw
   ↓
ev.disabled[key] = false
   ↓
ApplyPropertiesToRealEvents
   ↓
unsavedChanges = true
   ↓
UpdatePropertyText / RefreshEventPanel
```

새 이벤트 편집 기능을 만들 때는 이 흐름을 복사해서 시작한 뒤, 해당 이벤트 타입에 맞게 줄이는 편이 안전합니다.

### `LevelEventCompat` 패턴

Euclid는 우선 내부 `data` 딕셔너리를 보고, 실패하면 인덱서를 사용합니다.

```csharp
LevelEventCompat.TryGetRaw(ev, "position", out var raw);
LevelEventCompat.SetRaw(ev, "position", new Vector2(x, y));
```

게임 업데이트 때 `LevelEvent` 내부 구조가 달라져도 기능 코드 전체를 바꾸지 않도록 하기 위한 패턴입니다.

---

## 7. Harmony는 필요할 때만

UMM을 쓴다고 해서 Harmony 패치가 반드시 필요한 것은 아닙니다.

Euclid처럼 다음만으로 구현 가능한 기능도 많습니다.

- `MonoBehaviour.Update`
- `OnGUI`
- 현재 씬 오브젝트 조회
- 에디터 UI에 직접 오브젝트 추가
- `LevelEvent` 데이터 읽기/쓰기

게임 메서드의 실행 전후를 가로채야 할 때 Harmony를 사용합니다.

예:

- 특정 게임 메서드가 호출될 때 자동 처리
- 기존 반환값 변경
- 원래 실행 전/후 추가 로직
- 원본 로직 일부 교체

Harmony:

- https://github.com/pardeike/Harmony

패치가 많아질수록 버전 의존성이 커지므로 “Update에서 상태를 읽으면 충분한가?”를 먼저 확인하는 편이 유지보수에 유리합니다.

---

## 8. 에디터 탭/UI를 만드는 방법

### 선택지 A: 기존 라이브러리를 사용

과거에는 `EditorTabLib`가 많이 사용됐습니다.

- https://github.com/tjwogud/EditorTabLib

해당 저장소는 현재 아카이브 상태이므로 새 모드에서 런타임 의존성으로 채택할 때는 유지보수 상태를 먼저 확인해야 합니다.

### 선택지 B: 게임 UI를 직접 복제/확장

Euclid는 3.3.0에서 외부 탭 라이브러리 없이 다음 방식으로 구현했습니다.

1. 현재 `InspectorPanel`을 찾습니다.
2. 기존 inspector tab 중 적절한 오브젝트를 template으로 찾습니다.
3. `Instantiate`로 복제합니다.
4. 게임 전용 스크립트 중 필요 없는 것을 제거합니다.
5. 기존 이미지/TMP 스타일을 보존합니다.
6. 자체 클릭 핸들러를 연결합니다.
7. 자체 panel GameObject를 inspector panel 영역에 추가합니다.
8. built-in tab과 같은 선택/비선택 시각 상태를 모방합니다.

관련 파일:

```text
EuclidPanel.cs
EuclidPanel.UiFactory.cs
EuclidPanel.Style.cs
GameCompat.cs
```

### 왜 “직접 새 버튼을 그리는 것”보다 clone이 나은 경우가 많은가

게임의 스프라이트, 폰트, padding, transition, selected/unselected tint를 그대로 얻을 수 있기 때문입니다. ADOFAI UI 업데이트가 있어도 어느 정도 자연스럽게 따라갈 가능성이 있습니다.

다만 clone한 오브젝트에 원래 게임의 `MonoBehaviour`가 남아 있으면 예상치 못한 동작을 할 수 있습니다. Euclid의 `StripTemplateScripts`/`KeepTemplateBehaviour` 같은 정리 패턴을 참고합니다.

---

## 9. UI를 만들 때 배운 점

### IMGUI와 Unity UI Canvas는 같은 정렬 체계가 아님

`GUI.depth`는 IMGUI끼리의 순서를 조정하는 용도입니다. ADOFAI inspector가 Unity Canvas 기반이라면 `GUI.depth`만으로 “게임 위, inspector 아래” 같은 정확한 레이어를 만들기 어렵습니다.

Euclid의 최종 구조:

```text
게임 월드/타일
    ↓
Euclid construction/effect overlay Canvas
    ↓
ADOFAI editor UI Canvas
```

이 때문에 도형/효과 시각화는 `ConstructionShapeCanvasOverlay.cs`에서 별도 Canvas로 그립니다.

반면 마우스 이벤트를 기존 IMGUI 흐름에서 먼저 먹어야 하는 일부 상호작용은 `OnGUI`를 여전히 사용합니다.

즉 “렌더링은 Canvas, 필요한 입력 가로채기는 OnGUI”처럼 역할을 나눌 수 있습니다.

### 작은 UI는 기존 게임 스타일을 복제

새로 만든 Unity `Button`, `TMP_InputField`는 기본 상태로 놓으면 ADOFAI UI와 매우 다르게 보입니다. 기존 inspector control을 clone하거나 sprite/color/font 정보를 추출해서 적용하면 훨씬 자연스럽습니다.

### 레이아웃을 자동 LayoutGroup에 전부 맡기지 않기

떠 있는 detail panel처럼 위치/높이가 중요한 UI는 상위 inspector 레이아웃에 넣으면 강제로 늘어나거나 잘릴 수 있습니다. Euclid는 detail panel을 Canvas 쪽에 분리하고 host panel의 world corners를 기준으로 정렬합니다.

---

## 10. 입력 처리 순서가 중요한 경우

도형 점과 타일이 같은 위치에 있을 때 “점 선택을 우선하고 싶다” 같은 요구가 생길 수 있습니다.

Euclid는 `Update`에서 대략 다음 순서로 처리합니다.

```text
맵 변경 감지
   ↓
그려진 construction point 클릭 소비
   ↓
타일 selection order 동기화
   ↓
snapshot 갱신
   ↓
panel refresh
```

핵심은 **게임의 타일 클릭 결과를 읽기 전에 모드가 우선 처리해야 할 입력이 있는지 결정하는 것**입니다.

이런 규칙은 기능별 클래스에 흩뿌리지 말고 `MyModBehaviour.Update` 같은 한 곳에서 순서를 명시하는 것이 좋습니다.

---

## 11. 월드 좌표와 타일 좌표를 구분

ADOFAI 에디터 기능을 만들다 보면 다음 좌표가 섞입니다.

```text
타일 단위 좌표
월드 좌표
Screen 좌표
IMGUI 좌표(top-left origin)
Canvas local 좌표
```

Euclid에서 반복적으로 필요했던 변환:

```text
tile units × tile size → world
world → Camera.WorldToScreenPoint
IMGUI mouse y → Screen.height - mouseY
```

좌표 단위를 변수/메서드 이름에서 명시하는 것을 권장합니다.

예:

```csharp
Vector2 worldPoint
Vector2d tileOffset
Vector2 screenPoint
```

Euclid의 `Vector2d`는 기하 계산에서 float 오차가 빨리 누적되는 것을 줄이기 위해 double 기반 좌표를 별도로 유지합니다.

---

## 12. 상태 / 계산 / 렌더링 / 게임 변경을 분리

기하학이나 분석 기능을 만드는 경우 특히 중요합니다.

Euclid 예:

```text
ConstructionShapeTool.cs
  - 도형 상태
  - 선/원/교점 등의 계산

ConstructionShapeCanvasOverlay.cs
  - 화면에 그리기

CoordinateSnapTool.cs
  - 계산 결과를 실제 ADOFAI 이벤트 좌표에 적용

EuclidPanel.Construction.cs
  - 사용자가 조작하는 UI
```

이렇게 두면 “선 교점 계산이 틀린 것”과 “화면 표시가 틀린 것”과 “게임 값 쓰기가 틀린 것”을 분리해서 찾을 수 있습니다.

새 모드를 만들 때도 가능하면 다음 규칙을 지킵니다.

```text
계산 함수가 GameObject를 직접 만들지 않음
렌더러가 LevelEvent 값을 직접 수정하지 않음
UI 버튼이 reflection을 직접 하지 않음
compat layer가 UI를 만들지 않음
```

---

## 13. 에디터 맵이 바뀔 때 상태 처리

씬이 같아도 에디터에서 다른 `.adofai` 파일을 열 수 있으므로 Unity scene change만 보는 것으로는 부족할 수 있습니다.

Euclid는 `GameCompat.GetEditorLevelIdentity`로 현재 에디터 레벨을 식별하고 참조가 바뀌면 자체 상태를 정리합니다.

새 모드에서 다음 같은 상태가 있으면 맵 전환 정책을 명시해야 합니다.

- 선택한 타일 번호
- 캐시된 `scrFloor`
- 캐시된 `LevelEvent`
- 가이드/도형
- 분석 결과
- undo 관련 참조

오래된 `LevelEvent`/`scrFloor` 참조를 다음 맵에서 계속 쓰는 것은 피해야 합니다.

---

## 14. Localization은 모드 안에 넣어도 충분함

Euclid는 외부 Localization 모드 없이 embedded resource를 사용합니다.

```text
Localization/
├─ en.lang
├─ ko.lang
├─ ja.lang
└─ ...
```

형식:

```text
button.apply\tApply
button.clear\tClear
```

`.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Localization\*.lang">
    <WithCulture>false</WithCulture>
  </EmbeddedResource>
</ItemGroup>
```

런타임에서는 `RDString.language`를 읽어 locale code에 매핑하고, 없는 언어/키는 영어로 fallback합니다.

권장 규칙:

1. 영어 파일을 기준 키 목록으로 사용
2. 다른 모든 언어는 같은 키 집합을 가져야 함
3. 코드에 표시 문자열을 직접 쓰지 말고 key를 사용
4. 모드 이름처럼 번역하지 않을 브랜드 문자열은 별도 정책을 정함

Euclid의 구현:

```text
EuclidText.cs
Localization/*.lang
```

---

## 15. UMM Options와 모드 내부 UI를 구분

UMM 설정창은 `ModEntry.OnGUI`에서 IMGUI로 쉽게 만들 수 있습니다.

```csharp
entry.OnGUI = OnGui;
entry.OnSaveGUI = OnSaveGui;
```

영구 설정, 디버그 옵션, 색상, 기능 on/off 같은 것은 UMM Options에 두기 좋습니다.

반면 에디터 작업 중 계속 만지는 기능은 editor panel 안에 두는 것이 자연스럽습니다.

Euclid 기준:

```text
UMM Options
- camera frame 표시
- overlay 색상

Editor Euclid tab
- 도형 생성/편집
- P1/P2 선택
- snapping 조작
```

---

## 16. Settings 저장

UMM의 설정 베이스 클래스를 쓸 수도 있지만, Euclid는 버전/의존성 문제를 줄이기 위해 작은 `Settings.json`을 직접 읽고 씁니다.

설정 스키마가 바뀌면 이전 key를 읽어서 새 key에 migrate하는 코드를 둘 수 있습니다.

중요한 점:

- 파일이 없을 때 default 생성
- 잘못된 값일 때 fallback
- 새 버전에서 필드가 추가되어도 기존 파일이 깨지지 않게 함
- 색상/숫자 문자열은 parse 실패를 정상 상태로 취급

---

## 17. 로그를 적극적으로 남기기

UMM entry에서 logger를 보관합니다.

```csharp
internal static UnityModManager.ModEntry.ModLogger Logger { get; private set; }
```

권장 로그:

```text
모드 로드 성공
Application.version
Application.unityVersion
중요 reflection fallback 실패
설정 파일 parse 실패
에디터 API 호출 예외
```

Euclid는 시작 시 게임/Unity 버전을 로그에 남겨서 사용자가 “업데이트 후부터 안 됨”이라고 할 때 기준을 확인할 수 있게 합니다.

---

## 18. 빌드 후 UMM ZIP 자동 생성

직접 `Mods/MyMod`에 복사하는 개발 방식은 빠르지만, 배포 형태와 개발 형태가 달라져서 실수하기 쉽습니다.

현재 Euclid는 Release 빌드 후 바로 UMM용 ZIP을 만듭니다.

추천 흐름:

```text
소스 수정
   ↓
dotnet build -c Release
   ↓
dist/MyMod-x.y.z.zip
   ↓
UMM에서 ZIP 설치
```

MSBuild target 예:

```xml
<Target Name="CreateUmmRelease"
        AfterTargets="Build"
        Condition="'$(Configuration)' == 'Release' and '$(OS)' == 'Windows_NT'">
  <PropertyGroup>
    <UmmReleaseRoot>$(MSBuildProjectDirectory)\dist</UmmReleaseRoot>
    <UmmReleaseDir>$(UmmReleaseRoot)\MyMod</UmmReleaseDir>
    <UmmReleaseZip>$(UmmReleaseRoot)\MyMod-$(Version).zip</UmmReleaseZip>
  </PropertyGroup>

  <RemoveDir Directories="$(UmmReleaseDir)" Condition="Exists('$(UmmReleaseDir)')" />
  <MakeDir Directories="$(UmmReleaseDir)" />
  <Copy SourceFiles="$(TargetPath)" DestinationFiles="$(UmmReleaseDir)\MyMod.dll" />
  <Copy SourceFiles="$(MSBuildProjectDirectory)\Info.json"
        DestinationFiles="$(UmmReleaseDir)\Info.json" />
</Target>
```

Euclid의 실제 구현은 `Euclid.csproj`를 참고합니다.

---

## 19. 릴리스 전에 최소한 확인할 것

### 정적 확인

```text
Info.json JSON 문법
Id / AssemblyName / EntryMethod
Info.json Version ↔ csproj Version
old namespace/old mod name 잔존 여부
불필요한 PackageReference 여부
Localization key 누락 여부
release ZIP 내부 구조
```

### 게임 안에서 확인

```text
UMM에서 로드 성공
mod enable/disable
씬 진입/이탈
에디터 진입
새 맵/다른 맵 열기
패널 열기/닫기
마우스 입력
Undo
저장 후 재열기
에디터 재생 중 overlay 표시 정책
다른 해상도/aspect
언어 변경
```

Reflection을 쓰는 곳은 컴파일 성공만으로 확인할 수 없습니다. 런타임 테스트가 필수입니다.

---

## 20. ADOFAI 업데이트가 나오면 어디부터 볼 것인가

추천 순서:

```text
1. 새 게임 Managed DLL로 다시 컴파일
2. 컴파일 에러 목록 정리
3. Assembly-CSharp.dll에서 변경된 타입 검색
4. GameCompat / LevelEventCompat 수정
5. UMM 로그로 reflection 실패 확인
6. 에디터 UI hierarchy 다시 확인
7. 데이터 read 테스트
8. 데이터 write + undo + save 테스트
9. overlay sorting/input 테스트
```

기능별 파일을 바로 고치기보다 compat layer부터 맞추는 것이 중요합니다.

---

## 21. Euclid에서 실제로 버린 접근과 이유

다음 시행착오는 다른 모드에서도 참고할 만합니다.

### 외부 EditorTabLib 의존

처음에는 편하지만 특정 게임/라이브러리 버전에 동시에 묶입니다. Euclid는 자체 탭 구현으로 전환했습니다.

### ADOFAI native ColorField clone + native picker 강제 호출

내부 `ColorField`, `RDColorPickerPopup`, `BuildPickerData`를 reflection으로 연결하는 방식을 시도했지만, clone된 UI hierarchy와 popup reference가 안정적이지 않았습니다.

최종적으로는 모드 내부에 간단한 inline RGBA/HEX 편집기를 만드는 편이 더 안정적이었습니다.

교훈: “게임의 내부 UI를 재사용하면 무조건 더 안정적”인 것은 아닙니다. 단순 UI라면 직접 구현하는 것이 버전 호환성이 더 좋을 수 있습니다.

### 모든 overlay를 OnGUI로 그리기

ADOFAI editor Canvas와 정렬 관계가 원하는 대로 되지 않았습니다. 최종적으로 world와 editor UI 사이에 별도 Canvas를 두었습니다.

### UI 기능을 한 파일에 계속 추가

panel 코드가 너무 커져 partial 파일로 역할을 분리했습니다. 새 모드도 한 파일이 500~1000줄 이상 커지기 전에 lifecycle/UI factory/style/interaction 정도를 나누는 것이 좋습니다.

### 사용하지 않는 과거 fallback을 계속 보관

UI 구현을 교체한 뒤 old native picker, popup picker, IMGUI helper가 소스에 남아 유지보수를 방해했습니다. 0.7.61에서 제거했습니다.

교훈: fallback은 실제로 호출되는지 주기적으로 확인하고, 이미 대체된 실험 코드는 삭제합니다. Git이 과거 기록 역할을 하게 하는 편이 낫습니다.

---

## 22. 참고할 GitHub 저장소

### UMM 자체

Unity Mod Manager:

https://github.com/newman55/unity-mod-manager

UMM의 모드 작성 Wiki:

https://github.com/newman55/unity-mod-manager/wiki/How-to-create-a-mod-for-unity-game

`Info.json`, `EntryMethod`, UMM lifecycle을 확인할 때 우선 봅니다.

### 한국어 ADOFAI 모드 개발 가이드

FLOWERs-Modding / ADOFAI-Mod-Development-Guide:

https://github.com/FLOWERs-Modding/ADOFAI-Mod-Development-Guide

프로젝트 설정, 패치, GUI, 설정창, 빌드, 게임 코드 확인 등 기초 흐름을 한글로 볼 수 있습니다.

### 기본 템플릿 참고

PizzaLovers007 / AdofaiModTemplate:

https://github.com/PizzaLovers007/AdofaiModTemplate

UMM용 ADOFAI 프로젝트 구조, `Info.json`, release packaging, game DLL reference 방식을 비교할 때 좋습니다. 다만 오래된 템플릿은 현재 게임/Unity 버전에 맞게 참조 DLL과 빌드 설정을 갱신해야 합니다.

### 규모가 큰 실제 모드 예시

PizzaLovers007 / AdofaiTweaks:

https://github.com/PizzaLovers007/AdofaiTweaks

여러 tweak, Harmony patch, localization, UMM 설정 등 큰 모드 구조를 볼 때 참고할 수 있습니다.

adofaiex / JipperOverlayer:

https://github.com/adofaiex/JipperOverlayer

최근 ADOFAI 버전과 UMM/MelonLoader를 함께 고려하는 비교적 현대적인 프로젝트 구조를 볼 때 참고하기 좋습니다.

### Editor tab 역사 참고

tjwogud / EditorTabLib:

https://github.com/tjwogud/EditorTabLib

custom editor tab 구현 아이디어와 예제를 볼 수 있습니다. 저장소가 archive 상태이므로 새 프로젝트의 필수 dependency로 채택하기보다는 역사적 구현 참고용으로 보는 편이 안전합니다.

### 역컴파일

ILSpy:

https://github.com/icsharpcode/ILSpy

### 런타임 패치

Harmony:

https://github.com/pardeike/Harmony

---

## 23. AI에게 새 ADOFAI 모드를 만들어 달라고 할 때 같이 줄 정보

다음 내용을 처음부터 주면 시행착오가 많이 줄어듭니다.

```text
게임 버전: ADOFAI 3.3.0
Unity 버전: 6000.3.10f1
모드 로더: Unity Mod Manager
TargetFramework: net48
게임 경로: ...\A Dance of Fire and Ice
Managed 경로: ...\A Dance of Fire and Ice_Data\Managed
기존 모드 dependency 허용 여부: standalone / allowed
에디터 기능인지 플레이 기능인지
수정해야 하는 LevelEvent 종류
UI를 UMM Options에 넣을지 Editor panel에 넣을지
배포 형태: UMM drag-and-drop ZIP
```

그리고 가능하면 현재 게임의 `Assembly-CSharp.dll`을 같이 제공하는 것이 좋습니다. 게임 내부 API를 기억이나 오래된 문서로 추측하지 않고 실제 대상 버전을 기준으로 확인할 수 있기 때문입니다.

새 모드가 Euclid와 비슷한 에디터 기능이라면 이 파일들도 함께 참고 대상으로 지정하면 됩니다.

```text
GameCompat.cs
LevelEventCompat.cs
EuclidPanel.cs
EuclidPanel.UiFactory.cs
EuclidPanel.Style.cs
CameraFrameEditor.cs
Euclid.csproj
Info.json
```

---

## 24. 새 모드 시작용 최소 체크리스트

```text
[ ] mod 이름/namespace 결정
[ ] Info.json 작성
[ ] Startup.Load 연결
[ ] net48 csproj 작성
[ ] GameDir/ManagedAssembliesDir 속성 설정
[ ] Assembly-CSharp/UnityModManager 참조
[ ] 필요한 Unity 모듈만 추가
[ ] Mod class에 Logger/OnToggle 연결
[ ] persistent MonoBehaviour가 필요하면 생성
[ ] GameCompat 파일을 미리 만듦
[ ] Release build 성공
[ ] UMM ZIP 생성
[ ] 게임에서 load log 확인
[ ] 기능 구현 시작
```

에디터 기능이면 추가:

```text
[ ] scnEditor 관련 API를 현재 Assembly-CSharp에서 확인
[ ] selected floor/event 구조 확인
[ ] Undo/dirty/refresh 흐름 확인
[ ] tab/panel UI 방식 결정
[ ] map change 시 캐시 초기화 정책 결정
[ ] UI Canvas와 overlay sorting 정책 결정
```

이 정도를 템플릿으로 두면 다음 ADOFAI 모드는 Euclid 초반처럼 프로젝트 구조와 에디터 API를 다시 하나씩 찾는 시간을 상당히 줄일 수 있습니다.
