# ADOFAI 모드 개발 메모 — Euclid에서 정리한 실전 구조

이 문서는 Euclid를 ADOFAI 3.3.0에서 만들면서 확인한 구조를 바탕으로, 다음 얼불춤 모드를 시작할 때 프로젝트 구조와 에디터 내부 API를 다시 처음부터 찾지 않도록 정리한 개발 메모입니다.

기준 환경:

- A Dance of Fire and Ice 3.3.0
- Unity 6000.3.10f1
- Unity Mod Manager(UMM)
- Harmony (`0Harmony.dll`, UMM 설치본 사용)
- C# / .NET Framework 4.8 (`net48`)
- Windows Steam 설치본

ADOFAI의 `Assembly-CSharp.dll` 내부 클래스는 정식 모드 API가 아닙니다. 게임 버전이 바뀌면 클래스, 필드, 메서드, 코루틴 시그니처, 이벤트 저장 형식, UI 계층이 달라질 수 있습니다. 아래 내용은 3.3.0 기준의 실전 참고 자료로 사용해야 합니다.

---

## 1. UMM 모드의 기본 구조

UMM은 소스 코드를 직접 실행하지 않습니다. 컴파일된 DLL과 `Info.json`을 읽습니다.

배포 ZIP은 보통 다음 구조를 사용합니다.

```text
MyMod-1.0.0.zip
└─ MyMod/
   ├─ Info.json
   └─ MyMod.dll
```

Euclid 1.0.0도 같은 구조입니다.

```text
Euclid-1.0.0.zip
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

진입점은 얇게 유지하는 편이 좋습니다.

```csharp
using UnityModManagerNet;

namespace MyMod
{
    internal static class Startup
    {
        internal static void Load(UnityModManager.ModEntry modEntry)
        {
            MyModMain.Load(modEntry);
        }
    }
}
```

UMM 설정, 영구 `MonoBehaviour`, Harmony 패치 설치, 기능별 서비스 초기화는 별도 클래스로 나누는 것이 유지보수에 유리합니다.

Euclid의 대응 파일:

```text
Startup.cs
EuclidMod.cs
EuclidBehaviour.cs
EditorLevelLoadPatch.cs
```

---

## 2. 추천 프로젝트 역할 분리

작은 모드는 다음 정도면 충분합니다.

```text
MyMod/
├─ MyMod.csproj
├─ Info.json
├─ Startup.cs
├─ MyMod.cs
├─ MyModBehaviour.cs
├─ GameCompat.cs
├─ README.md
└─ BUILD_RELEASE.cmd
```

에디터 기능이 커지면 역할별로 나눕니다.

```text
UMM / bootstrap
      ↓
runtime coordination (Update / input / hooks)
      ↓
feature state + geometry ──→ renderer
      ↓
ADOFAI data mutation
      ↓
compatibility / integration layer
```

핵심 규칙:

```text
계산 함수가 UI를 만들지 않음
렌더러가 LevelEvent를 수정하지 않음
UI 콜백이 private reflection을 직접 하지 않음
compat layer가 기능 상태를 소유하지 않음
```

Euclid에서는 큰 패널 클래스를 partial 파일로 나눠 lifecycle, construction UI, interaction, style, UI factory를 분리합니다.

---

## 3. `.csproj`에서 중요한 부분

게임 설치본의 Managed DLL을 직접 참조하면 현재 게임 버전과 맞는 타입에 컴파일할 수 있습니다.

기본 구조:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <OutputType>Library</OutputType>
    <AssemblyName>MyMod</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <PropertyGroup>
    <GameDir Condition="'$(GameDir)' == '' and '$(ADOFAI_DIR)' != ''">$(ADOFAI_DIR)</GameDir>
    <GameDir Condition="'$(GameDir)' == ''">C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice</GameDir>
    <ManagedAssembliesDir>$(GameDir)\A Dance of Fire and Ice_Data\Managed</ManagedAssembliesDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="0Harmony">
      <HintPath>$(ManagedAssembliesDir)\UnityModManager\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ManagedAssembliesDir)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityModManager">
      <HintPath>$(ManagedAssembliesDir)\UnityModManager\UnityModManager.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

기능에 따라 Unity 모듈을 추가합니다. Euclid는 UI, TMP, IMGUI, Input을 사용하므로 다음 계열을 참조합니다.

```text
RDTools.dll
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
```

게임/Unity DLL은 게임에 이미 있으므로 일반적으로 `<Private>false>`를 사용해 배포 폴더에 복사되지 않게 합니다.

Steam 라이브러리가 다르면 프로젝트 파일을 수정하기보다 `GameDir`을 넘깁니다.

```bat
dotnet build MyMod.csproj -c Release "-p:GameDir=D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice"
```

---

## 4. 무엇을 역컴파일해서 볼 것인가

가장 먼저 확인할 대상:

```text
A Dance of Fire and Ice_Data\Managed\Assembly-CSharp.dll
```

Euclid에서 자주 확인한 타입:

```text
scnEditor
InspectorPanel
scrFloor
ADOFAI.LevelEvent
LevelEventType
RDString
```

에디터 동작을 만들 때는 클래스 이름을 추측하기보다 실제 DLL에서 게임의 동작 경로를 따라가는 편이 안전합니다.

예를 들어 이벤트 값을 수정한다면:

1. 현재 선택 이벤트와 inspector가 어디에 저장되는지 확인
2. `LevelEvent` 원시 데이터 형식 확인
3. 기본 에디터가 같은 값을 수정할 때 호출하는 메서드 추적
4. Undo, 실제 이벤트 적용, unsaved 표시, UI refresh 순서 확인
5. 필요한 최소 접근만 compat layer로 옮김

역컴파일 도구로는 ILSpy가 편합니다.

- https://github.com/icsharpcode/ILSpy

타입 이름뿐 아니라 실제 UI 문자열, 이벤트 키(`position`, `positionOffset`, `relativeTo`)와 메서드 이름을 같이 검색하면 빠릅니다.

---

## 5. `GameCompat` / `LevelEventCompat`를 두는 이유

게임 내부 멤버 접근이 여러 기능 파일에 흩어지면 게임 업데이트 시 수정 범위가 급격히 커집니다.

권장 구조:

```csharp
internal static class GameCompat
{
    internal static bool TrySaveState(scnEditor editor) { ... }
    internal static object GetSettingsPanel(scnEditor editor) { ... }
}
```

기능 코드는 의미 단위로 읽히게 합니다.

```csharp
GameCompat.TrySaveState(editor);
LevelEventCompat.SetRaw(ev, "position", value);
GameCompat.TryRefreshEventPanel(editor, ev);
```

Euclid에서 우선 볼 파일:

```text
GameCompat.cs
LevelEventCompat.cs
EditorLevelLoadPatch.cs
CameraFrameEditor.cs
```

`LevelEventCompat`는 raw dictionary/indexer 차이, enum wrapper, property enabled/disabled 표현을 흡수하는 역할을 합니다.

---

## 6. `LevelEvent`는 값만 대입해서 끝나지 않을 수 있음

에디터 이벤트 편집은 다음 상태가 같이 움직일 수 있습니다.

```text
Undo
raw LevelEvent data
property enabled/disabled state
real/applied event
unsaved state
inspector text
floor transform
```

일반적인 discrete edit는 대략 다음 흐름입니다.

```text
SaveState
→ raw 값 쓰기
→ 필요한 경우 property enable/disable 조정
→ host apply/commit
→ unsaved 표시
→ inspector/property refresh
```

모든 편집에서 무조건 `disabled[key] = false`를 하면 안 됩니다. 저장된 값만 바꾸는 작업과 실제로 해당 속성을 활성화하는 작업을 구분해야 합니다.

PositionTrack처럼 적용 비용이 큰 기능은 연속 드래그와 discrete snap을 같은 방식으로 처리하지 않는 것이 중요합니다.

```text
연속 drag: raw preview 위주 → MouseUp에서 host commit
snap: Undo 1회 → 즉시 discrete commit
```

---

## 7. Harmony는 실제 lifecycle 경계가 필요할 때 사용

UMM 모드라고 해서 모든 기능에 Harmony가 필요한 것은 아닙니다.

다음은 `Update`, `OnGUI`, scene object 접근만으로도 구현할 수 있습니다.

- 현재 선택 상태 읽기
- 오버레이 렌더링
- 에디터 UI 추가
- 일부 이벤트 값 읽기/쓰기

반면 “정확히 이 게임 동작이 발생했을 때”를 알아야 하는 경우 Harmony가 더 안전할 수 있습니다.

Euclid의 대표 사례는 **에디터 레벨 로드**입니다.

처음에는 다음 신호를 조합해 맵 전환을 추정할 수 있어 보입니다.

```text
scnEditor.isLoading
ADOBase.levelPath
levelData object identity
floors / first floor identity
settings panel identity
```

하지만 실제 에디터는 객체를 재사용할 수 있고, `isLoading`이 Euclid의 두 `Update` 사이에서 켜졌다 꺼질 수도 있습니다. 따라서 이런 값은 보조 신호로만 쓰는 편이 안전합니다.

ADOFAI 3.3.0에서 실제 파일 로드 경로는 `scnEditor.OpenLevelCo`를 거칩니다. Euclid는 이 코루틴을 Harmony로 패치해 실제 로드가 시작될 때 map-local 상태를 초기화합니다.

중요한 구분:

```text
다른 .adofai 열기       -> 초기화
같은 .adofai 다시 열기  -> 초기화
Save / Save As           -> 유지
파일 선택창 열고 취소   -> 유지
```

파일 선택창을 여는 no-argument `OpenLevel()` 같은 더 이른 메서드를 패치하면 사용자가 취소했을 때도 상태가 사라질 수 있습니다. lifecycle hook은 이름보다 **실제로 작업이 확정되는 지점**을 기준으로 잡아야 합니다.

Harmony:

- https://github.com/pardeike/Harmony

---

## 8. 에디터 탭/UI를 만드는 방법

과거에는 `EditorTabLib`가 많이 사용됐습니다.

- https://github.com/tjwogud/EditorTabLib

저장소가 archive 상태이므로 새 프로젝트의 필수 runtime dependency로 채택하기 전 유지보수 상태를 확인해야 합니다.

Euclid는 외부 탭 라이브러리 없이 게임 UI를 직접 복제/확장합니다.

대략적인 흐름:

1. 현재 `InspectorPanel` 찾기
2. 기존 inspector tab을 template으로 선택
3. `Instantiate`
4. 불필요한 게임 전용 `MonoBehaviour` 제거
5. 이미지/TMP 스타일 보존
6. 자체 클릭 handler 연결
7. 자체 panel을 inspector 영역에 추가
8. built-in tab의 선택/비선택 시각 상태 모방

관련 파일:

```text
EuclidPanel.cs
EuclidPanel.UiFactory.cs
EuclidPanel.Style.cs
GameCompat.cs
```

기존 UI clone은 스프라이트, 폰트, padding, transition을 그대로 얻을 수 있다는 장점이 있지만, template에 남은 게임 스크립트가 예상치 못한 동작을 할 수 있으므로 반드시 정리해야 합니다.

---

## 9. Unity UI와 IMGUI를 역할별로 구분

`GUI.depth`는 IMGUI끼리의 순서를 조정합니다. ADOFAI inspector가 Unity Canvas라면 `GUI.depth`만으로 월드와 inspector 사이의 레이어를 정확히 만들기 어렵습니다.

Euclid의 기본 레이어 개념:

```text
게임 월드 / 타일
      ↓
Euclid overlay Canvas
      ↓
ADOFAI editor UI Canvas
```

도형/이펙트 표시처럼 지속 렌더링되는 요소는 Canvas가 적합하고, 기존 IMGUI 이벤트 흐름에서 클릭을 먼저 소비해야 하는 일부 상호작용은 `OnGUI`를 사용할 수 있습니다.

UI가 한 프레임씩 깜빡이거나 크기가 튀면 `LateUpdate`로 계속 보정하기 전에 생성 시점의 `LayoutElement`, `interactable`, `ColorBlock.fadeDuration`을 먼저 확인합니다.

권장:

```text
control 생성
→ 최종 크기 지정
→ 최종 enabled/selected 상태 지정
→ 최종 색상 지정
→ 렌더
```

---

## 10. 입력 처리 순서와 picker 상태

도형 점과 타일이 같은 위치에 있을 때 어떤 입력을 우선할지 명시해야 합니다.

Euclid는 그려진 construction Point 클릭을 먼저 처리한 뒤 타일 selection 결과를 읽습니다. 이렇게 하면 점과 타일이 겹쳐도 점 선택을 우선할 수 있습니다.

또한 “다음 클릭으로 위치 선택” 같은 기능은 UI 버튼 상태와 별개로 두지 말고 하나의 명확한 state machine으로 관리하는 것이 좋습니다.

Euclid의 endpoint Select 규칙:

```text
Select P1 클릭           -> P1 선택 대기
Select P1 다시 클릭      -> 취소
P1 대기 중 Select P2     -> P2 대기로 전환
실제 타일/점 선택 완료   -> 대기 종료
수동 좌표 입력            -> 해당 대기 종료
맵 로드                   -> 대기 종료
```

버튼의 켜짐 표시도 같은 pending state를 직접 반영해야 합니다.

---

## 11. 월드 좌표와 타일 좌표를 구분

ADOFAI 에디터에서 흔히 섞이는 좌표:

```text
tile-unit 좌표/offset
world 좌표
screen 좌표
IMGUI 좌표(top-left origin)
Canvas local 좌표
```

반복적으로 필요한 변환:

```text
tile units × tileSize → world
world → Camera.WorldToScreenPoint
IMGUI mouseY → Screen.height - mouseY
```

변수 이름에도 단위를 드러내는 편이 좋습니다.

```csharp
Vector2 worldPoint;
Vector2d tileOffset;
Vector2 screenPoint;
```

표시, hit test, snap preview, 실제 property write-back에서 같은 reference origin을 사용해야 합니다. 마커만 맞고 실제 결과가 틀리거나 반대로 움직이는 버그는 기준 좌표가 서로 다를 때 자주 발생합니다.

---

## 12. 상태 / 계산 / 렌더링 / 게임 변경을 분리

Euclid 예:

```text
ConstructionShapeTool.cs
  상태 + 기하 계산

ConstructionShapeCanvasOverlay.cs
  렌더링

CoordinateSnapTool.cs
  계산 결과를 ADOFAI 좌표 property로 변환

EuclidPanel.Construction.cs
  사용자 UI
```

이렇게 분리하면 “기하 계산”, “화면 projection”, “게임 값 적용” 문제를 따로 진단할 수 있습니다.

특히 read-only background overlay가 selected event의 편집 cache를 수정하지 않도록 해야 합니다.

---

## 13. 지원 범위를 명시적으로 제한하기

비슷한 프로퍼티 이름을 가진 이벤트라고 해서 같은 모델로 처리할 수 있는 것은 아닙니다.

Euclid 1.0.0의 coordinate overlay/snap 지원 범위:

```text
MoveCamera / CameraMove
MoveTrack
PositionTrack
FreeRoam / FreeRoamRemove
```

`MoveDecorations`는 의도적으로 제외합니다.

장식 이동은 태그로 여러 decoration을 동시에 대상으로 삼을 수 있고, placement/reference, 이전 위치, 시차 등의 상태에 따라 하나의 이벤트가 여러 서로 다른 위치를 가질 수 있습니다. 따라서 generic vector-property fallback으로 단일 좌표 마크를 만드는 것은 의미적으로 잘못될 수 있습니다.

교훈: API 구조가 비슷해 보여도 **사용자에게 표시하려는 의미가 하나로 정의되는지** 먼저 확인합니다.

---

## 14. PositionTrack에서 raw 값과 applied 상태를 구분

`PositionTrack`은 저장된 `positionOffset`과 실제 floor transform이 같은 프레임에 갱신되지 않을 수 있습니다.

또한 property가 disabled라면 raw 값은 남아 있어도 실제 적용값은 0입니다.

```text
rawOffset = 저장된 값

effectiveOffset =
    rawOffset  if enabled
    (0,0)      if disabled
```

`relativeTo = ThisTile`에서 이미 적용된 상태라면:

```text
referenceWorld = displayedFloorWorld - effectiveOffset * tileSize
targetWorld    = referenceWorld + effectiveOffset * tileSize
```

disabled 상태에서 raw 값을 빼면 기준점이 반대 방향으로 튀는 문제가 생깁니다.

편집 중에는 다음 세 상태를 구분하는 편이 안전합니다.

```text
applied state
pending raw edit
floor catch-up after host commit
```

숫자 값 자체가 0이라는 이유만으로 provisional 상태라고 판단하면 안 됩니다.

---

## 15. Localization과 Settings

Euclid는 외부 Localization 모드 없이 embedded resource를 사용합니다.

```text
Localization/
├─ en.lang
├─ ko.lang
├─ ja.lang
└─ ...
```

`.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Localization\*.lang">
    <WithCulture>false</WithCulture>
  </EmbeddedResource>
</ItemGroup>
```

영어 파일을 기준 키 목록으로 두고 다른 언어가 같은 키 집합을 갖게 하면 검증하기 쉽습니다.

설정은 다음 원칙을 지키면 버전 변경에 강합니다.

- 파일이 없으면 default
- parse 실패는 정상적인 fallback 경로로 처리
- 새 필드 추가 시 기존 Settings 파일과 호환
- 제거된 필드는 다음 저장 때 자연스럽게 사라지게 함
- 색상/숫자 문자열은 검증 후 적용

UMM Options에는 영구 설정을, 에디터 panel에는 작업 중 자주 조작하는 기능을 두는 편이 자연스럽습니다.

Euclid의 Options에는 카메라 프레임, 모든 지원 이펙트 마크 표시, 이펙트별 색상 설정 등이 있습니다.

---

## 16. 로그는 integration 실패를 찾는 도구

UMM logger를 보관하고 다음을 기록해두면 버전 문제를 찾기 쉽습니다.

```text
mod load 성공
game version
Unity version
Harmony hook 설치 결과
중요 reflection fallback 실패
settings parse 실패
ADOFAI API 호출 예외
```

특히 Harmony patch가 런타임에서 대상 메서드를 못 찾는 경우 빌드는 성공해도 기능이 조용히 실패할 수 있으므로 설치 결과 로그가 유용합니다.

---

## 17. Release ZIP 자동 생성

개발 DLL을 직접 Mods 폴더에 복사하는 방식만 쓰면 실제 배포 구조를 검증하지 못할 수 있습니다.

권장 흐름:

```text
소스 수정
   ↓
scripts/check_project.ps1
   ↓
dotnet build -c Release
   ↓
dist/MyMod-x.y.z.zip
   ↓
UMM에서 ZIP으로 설치
   ↓
최종 smoke test
```

Euclid의 `Euclid.csproj`는 Release 빌드 후 다음 구조의 ZIP을 자동 생성합니다.

```text
dist/Euclid-1.0.0.zip
└─ Euclid/
   ├─ Euclid.dll
   └─ Info.json
```

`Info.json`과 `.csproj`의 버전은 항상 같아야 합니다.

---

## 18. 릴리스 전 최소 테스트

정적 확인:

```text
Info.json JSON 문법
Id / AssemblyName / EntryMethod
Info.json Version == csproj Version
old namespace/branding 잔존 여부
Localization key 누락 여부
불필요한 runtime dependency 여부
release ZIP 내부 구조
```

게임 안에서 확인:

```text
UMM load / enable / disable
에디터 진입과 탭 열기
Point / Line / Circle 생성 및 편집
Select 재클릭 취소와 P1↔P2 전환
Pin / unpin
교점 생성
snap
PositionTrack enabled/disabled
PositionTrack drag / snap / undo
MoveCamera position / zoom / rotation
all-effect marker mode
MoveDecorations가 지원 목록에 나타나지 않는지
다른 맵 열기 / 같은 맵 reload
Save As에서 construction state가 유지되는지
파일 선택창 취소에서 construction state가 유지되는지
설정 저장 후 재시작
언어 변경
```

Reflection과 Harmony는 컴파일 성공만으로 검증할 수 없습니다. 실제 대상 게임에서 런타임 테스트가 필요합니다.

---

## 19. 게임 업데이트가 나오면 보는 순서

```text
1. 새 Managed DLL로 컴파일
2. compile error 정리
3. Assembly-CSharp.dll에서 변경 타입 검색
4. GameCompat / LevelEventCompat 확인
5. Harmony patch target 확인
6. 실제 editor level load 경로 확인
7. inspector hierarchy 확인
8. event read 테스트
9. event write + undo + save 테스트
10. overlay sorting / input 테스트
11. map lifecycle 테스트
12. UMM ZIP 설치 테스트
```

기능별 코드를 먼저 뜯어고치기보다 compatibility/integration layer를 먼저 맞추는 것이 좋습니다.

---

## 20. 피해야 할 접근

### private API 추측을 기능마다 반복

한 번 확인한 게임 내부 접근은 compat layer로 모읍니다.

### 모든 overlay를 OnGUI로 처리

Unity Canvas 기반 editor UI와 원하는 sorting 관계를 만들기 어려울 수 있습니다. 지속 렌더링과 입력 가로채기를 분리하는 편이 낫습니다.

### UI를 만든 뒤 매 프레임 크기 수정

최종 상태를 생성 시점에 정의할 수 있다면 그렇게 해야 합니다. per-frame layout repair는 실제 원인을 가릴 수 있습니다.

### generic property 이름만 보고 다른 event까지 지원

이벤트의 의미가 다르면 동일한 vector property가 있어도 같은 overlay로 표현할 수 없습니다.

### 맵 전환을 path 하나로만 판정

새 맵 Save As와 다른 맵 Open을 구분하기 어렵습니다. 반대로 object identity만 보면 게임의 재사용 정책 때문에 놓칠 수 있습니다. 가능하면 실제 load lifecycle을 hook합니다.

### 너무 이른 file-open hook

파일 선택창을 여는 시점에 state를 지우면 취소에도 데이터가 사라집니다. 실제 path가 확정되고 로드가 진행되는 지점을 사용합니다.

### 사용되지 않는 fallback/실험 코드를 계속 보관

현재 호출되지 않는 실험 구현은 삭제하고 Git 기록을 과거 보관소로 사용하는 편이 유지보수에 좋습니다.

---

## 21. 참고 저장소

Unity Mod Manager:

https://github.com/newman55/unity-mod-manager

UMM mod 작성 Wiki:

https://github.com/newman55/unity-mod-manager/wiki/How-to-create-a-mod-for-unity-game

한국어 ADOFAI 모드 개발 가이드:

https://github.com/FLOWERs-Modding/ADOFAI-Mod-Development-Guide

AdofaiModTemplate:

https://github.com/PizzaLovers007/AdofaiModTemplate

AdofaiTweaks:

https://github.com/PizzaLovers007/AdofaiTweaks

EditorTabLib 역사 참고:

https://github.com/tjwogud/EditorTabLib

ILSpy:

https://github.com/icsharpcode/ILSpy

Harmony:

https://github.com/pardeike/Harmony

---

## 22. AI에게 새 ADOFAI 모드를 만들게 할 때 같이 줄 정보

처음부터 다음을 알려주면 오래된 API를 추측하는 시행착오가 줄어듭니다.

```text
게임 버전: ADOFAI 3.3.0
Unity 버전: 6000.3.10f1
모드 로더: Unity Mod Manager
TargetFramework: net48
게임 경로: ...\A Dance of Fire and Ice
Managed 경로: ...\A Dance of Fire and Ice_Data\Managed
runtime dependency 허용 여부
에디터 기능인지 플레이 기능인지
수정할 LevelEvent 종류
필요한 lifecycle hook 종류
UI 위치: UMM Options / editor panel
배포 형태: UMM drag-and-drop ZIP
```

가능하면 대상 버전의 `Assembly-CSharp.dll` 또는 정확한 decompile 결과를 같이 제공하는 것이 가장 좋습니다.

Euclid와 비슷한 에디터 기능이면 다음 파일을 참고 대상으로 지정할 수 있습니다.

```text
GameCompat.cs
LevelEventCompat.cs
EditorLevelLoadPatch.cs
EuclidBehaviour.cs
EuclidPanel.cs
EuclidPanel.Construction.cs
CoordinateSnapTool.cs
CameraFrameEditor.cs
Euclid.csproj
Info.json
```

---

## 23. 새 모드 시작용 체크리스트

```text
[ ] mod 이름 / namespace 결정
[ ] Info.json 작성
[ ] Startup.Load 연결
[ ] net48 csproj 작성
[ ] GameDir / ManagedAssembliesDir 설정
[ ] Assembly-CSharp / UnityModManager 참조
[ ] Harmony가 필요하면 UMM의 0Harmony 참조
[ ] 필요한 Unity 모듈만 추가
[ ] Logger / OnToggle 연결
[ ] persistent MonoBehaviour 필요 여부 결정
[ ] GameCompat / LevelEventCompat 역할 정의
[ ] Release build 성공
[ ] UMM ZIP 생성
[ ] 게임에서 load log 확인
```

에디터 기능이면 추가:

```text
[ ] scnEditor API를 현재 Assembly-CSharp에서 확인
[ ] selected floor/event 구조 확인
[ ] Undo / dirty / refresh 흐름 확인
[ ] tab/panel UI 방식 결정
[ ] map-local state 정책 결정
[ ] 실제 level load lifecycle 확인
[ ] overlay sorting 정책 결정
[ ] input 우선순위 결정
[ ] Save / Open / reload / cancel 경계조건 테스트
```

이 정도를 기본 템플릿으로 두면 다음 ADOFAI 모드에서는 프로젝트 구조보다 실제 기능 구현에 더 빨리 들어갈 수 있습니다.
