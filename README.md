<details open>
<summary><strong>한국어</strong></summary>

# 유클리드

유클리드는 불과 얼음의 춤 에디터에서 작도, 좌표 확인, 이펙트 위치 시각화와 스냅을 도와주는 편집 보조 모드입니다.

## 주요 기능

- 점, 직선, 원 작도
- 도형 이름, 색상, 좌표 편집
- 도형 간 교점 생성
- 도형을 기준으로 이펙트 위치 스냅
- 카메라 이동, 길 이동, 길 위치, 자유 이동 구간의 위치 시각화
- 카메라 프레임 직접 이동, 확대·축소, 회전
- 선택한 지원 이펙트의 위치 좌표 마크 직접 드래그
- 모든 지원 이펙트의 위치 마크를 한 번에 표시
- 직선의 기울기, 절편, 각도와 원의 반지름 표시

## 유클리드 탭

에디터 왼쪽 탭에 추가되는 `Å` 아이콘을 눌러 유클리드 탭을 열 수 있습니다.

### 도형 목록

현재 맵에서 만든 도형이 목록에 표시됩니다.

- 클릭: 도형 선택
- 쉬프트+클릭: 범위 선택
- 컨트롤+클릭: 개별 도형 추가 선택 또는 선택 해제
- 켜짐/꺼짐: 해당 도형의 화면 표시 여부 변경

새 맵을 열면 이전 맵에서 만든 도형은 초기화됩니다.

목록 아래에는 다음 기능이 있습니다.

- 추가: 새 도형을 만듭니다.
- 삭제: 선택한 도형을 삭제합니다.
- 교점 생성: 선택된 도형 사이의 교점을 점 도형으로 생성합니다.
- 스냅: 선택한 하나의 도형을 기준으로 현재 이펙트의 위치를 이동합니다.

## 도형 정보

도형을 선택하면 화면 오른쪽에 도형 정보 패널이 표시됩니다.

### 이름

도형 이름을 직접 지정할 수 있습니다.

### 종류

다음 세 종류를 사용할 수 있습니다.

- 점: 첫 번째 점만 사용합니다.
- 직선: 첫 번째 점과 두 번째 점을 지나는 무한 직선을 만듭니다.
- 원: 첫 번째 점을 중심으로 하고 두 번째 점을 지나는 원을 만듭니다.

### 첫 번째 점과 두 번째 점

좌표는 직접 입력하거나 `선택` 버튼을 눌러 에디터의 타일 또는 이미 만든 점 도형에서 가져올 수 있습니다.

일반 선택은 현재 좌표를 한 번 복사하는 방식입니다. 이후 원본 타일이나 점이 움직여도 도형 좌표는 따라가지 않습니다.

`고정`을 켜면 선택한 타일이나 점을 실시간 기준으로 사용합니다. 원본 위치가 바뀌면 해당 도형의 점도 같이 따라갑니다.

점 도형에서는 두 번째 점 항목이 표시되지만 비활성화됩니다.

### 색상

도형별 색상을 직접 지정할 수 있습니다.

빨강, 초록, 파랑, 투명도를 포함한 16진수 색상값을 사용할 수 있습니다.

### 도형 수치

직선에서는 다음 값이 표시됩니다.

- `a`: 기울기
- `b`: y절편
- `θ`: 직선의 방향 각도

원의 경우 다음 값이 표시됩니다.

- `r`: 반지름

직선의 각도는 방향을 구분하지 않는 직선 기준으로 0도 이상 180도 미만 범위로 표시됩니다.

## 이펙트 위치 시각화

지원되는 이펙트를 선택하면 현재 이펙트의 위치 관계를 화면에 표시합니다.

기본 표현은 다음과 같습니다.

- 타일 위치 마크: 현재 이펙트의 위치 오프셋이 적용되기 전 기준 위치
- 위치 좌표 마크: 이펙트가 실제로 지정하는 위치
- 선분: 두 위치 사이의 이동량
- 이름: 현재 표시 중인 이펙트 종류

위치 좌표 마크는 직접 드래그해 이펙트의 위치를 바꿀 수 있습니다.

작도 도형 하나가 선택되어 있고 스냅이 켜져 있다면 드래그 중에도 해당 도형에 맞춰 위치를 이동할 수 있습니다.

지원하는 이펙트는 카메라 이동, 길 이동, 길 위치, 자유 이동 구간입니다.

## 카메라 이동 프레임

카메라 이동 이펙트를 선택하면 위치, 확대 정도, 회전을 반영한 카메라 프레임이 표시됩니다.

프레임은 화면에서 직접 조작할 수 있습니다.

- 가운데 핸들 드래그: 카메라 위치 이동
- 네 모서리 핸들 드래그: 확대·축소 조절
- 위쪽 회전 핸들 드래그: 카메라 회전 조절

확대 또는 회전 항목이 현재 이펙트에서 꺼져 있더라도, 처음 핸들을 움직일 때 현재 적용 중인 값을 기준으로 편집을 시작합니다.

## 길 위치 이펙트

- 타일 위치 마크: 현재 길 위치 이펙트 자체가 적용되기 전 타일 위치
- 위치 좌표 마크: 현재 길 위치 이펙트까지 적용된 최종 위치

`positionOffset` 항목이 꺼져 있으면 저장된 원시 오프셋 값은 유지되지만 화면 표시에서는 실효 오프셋을 0으로 취급합니다. 따라서 타일 위치 마크와 위치 좌표 마크가 복원된 타일 위치에 함께 표시됩니다.

## 모든 이펙트 마크 표시

유니티 모드 매니저의 옵션에서 `모든 이펙트 마크 표시`를 켜면 현재 선택한 이펙트뿐 아니라 맵에 있는 지원 이펙트의 위치 마크를 함께 볼 수 있습니다.

이 기능은 한 화면에 많은 이펙트를 표시할 수 있기 때문에 이펙트가 매우 많은 맵에서는 성능이 조금 떨어질 수 있습니다.

기본값은 꺼짐입니다.

## 기본 이펙트 색상

여러 종류의 이펙트를 동시에 표시했을 때 구분하기 쉽도록 기본 색상을 이펙트 종류별로 나눴습니다.

- 카메라 이동: 빨강
- 길 이동: 노랑
- 길 위치: 초록
- 자유 이동 구간: 파랑

기본 팔레트에서는 타일 위치 마크, 위치 좌표 마크, 선분, 이름에 같은 계열 색과 같은 투명도를 사용합니다. 역할 구분은 마크의 모양으로 합니다.

## 옵션

유니티 모드 매니저 옵션창에서 다음 항목을 설정할 수 있습니다.

- 카메라 프레임 표시 여부
- 모든 이펙트 마크 표시 여부
- 카메라 프레임 색상
- 이펙트별 타일 위치 마크 색상
- 이펙트별 위치 좌표 마크 색상
- 이펙트별 선분 색상
- 이펙트별 이름 색상

</details>

<details>
<summary><strong>English</strong></summary>

# Euclid

Euclid is an editor utility mod for A Dance of Fire and Ice that provides geometric construction tools, coordinate inspection, effect-position visualization, and snapping.

## Features

- Construct points, lines, and circles
- Edit shape names, colors, and coordinates
- Create intersections between shapes
- Snap effect positions to constructed shapes
- Visualize positions for Move Camera, Move Track, Position Track, and Free Roam sections
- Move, zoom, and rotate the camera frame directly
- Drag the position marker of the selected supported effect directly
- Display position markers for all supported effects at once
- Display line slope, y-intercept, angle, and circle radius

## Euclid Tab

Open the Euclid tab by clicking the `Å` icon added to the left side of the editor.

### Shape List

Shapes created in the current level are shown in the list.

- Click: select a shape
- Shift+Click: range selection
- Ctrl+Click: add or remove individual shapes from the selection
- On/Off: toggle whether the shape is visible in the editor

Shapes created in the previous level are cleared when a new level is opened.

The following actions are available below the list.

- Add: creates a new shape.
- Delete: deletes the selected shapes.
- Create Intersections: creates point shapes at intersections between the selected shapes.
- Snap: moves the current effect position using one selected shape as the snapping target.

## Shape Info

Selecting a shape opens the Shape Info panel on the right side of the screen.

### Name

You can assign a custom name to each shape.

### Type

Three shape types are available.

- Point: uses only the first point.
- Line: creates an infinite line through the first and second points.
- Circle: creates a circle centered on the first point and passing through the second point.

### First and Second Points

Coordinates can be entered directly, or copied from an editor tile or an existing point shape using the `Select` button.

A normal selection copies the current coordinates once. The shape does not follow the source tile or point if it moves later.

When `Pin` is enabled, the selected tile or point becomes a live reference. If the source position changes, the corresponding point of the shape follows it.

For point shapes, the second-point controls remain visible but are disabled.

### Color

Each shape can have its own color.

Hexadecimal color values including red, green, blue, and alpha channels are supported.

### Shape Values

Lines display the following values.

- `a`: slope
- `b`: y-intercept
- `θ`: line direction angle

Circles display the following value.

- `r`: radius

Because a line has no intrinsic direction, its angle is displayed in the range from 0 degrees inclusive to 180 degrees exclusive.

## Effect Position Visualization

When a supported effect is selected, Euclid displays the positional relationship of that effect in the editor.

The default visualization consists of:

- Tile position marker: the reference position before the current effect's position offset is applied
- Position coordinate marker: the position actually specified by the effect
- Segment: the displacement between the two positions
- Name: the type of effect currently being visualized

The position coordinate marker can be dragged directly to change the effect position.

If one construction shape is selected and snapping is enabled, the dragged position can also snap to that shape.

Supported effects are Move Camera, Move Track, Position Track, and Free Roam sections.

## Move Camera Frame

Selecting a Move Camera effect displays a camera frame that reflects its position, zoom, and rotation.

The frame can be manipulated directly in the editor.

- Drag the center handle: move the camera position
- Drag any of the four corner handles: adjust zoom
- Drag the upper rotation handle: adjust camera rotation

Even if the zoom or rotation property is disabled on the current effect, moving its handle starts editing from the value currently in effect.

## Position Track Effect

- Tile position marker: the tile position before the current Position Track effect itself is applied
- Position coordinate marker: the final position after the current Position Track effect is applied

When `positionOffset` is disabled, the stored raw offset value is preserved, but the visualization treats the effective offset as zero. The tile position marker and position coordinate marker therefore appear together at the restored tile position.

## Show All Effect Markers

Enable `Show All Effect Markers` in the Unity Mod Manager options to display position markers for all supported effects in the level, not only the currently selected effect.

Because this can display many effects at once, performance may decrease slightly on levels with a very large number of effects.

This option is disabled by default.

## Default Effect Colors

Default colors are assigned by effect type so different effects remain easy to distinguish when displayed together.

- Move Camera: red
- Move Track: yellow
- Position Track: green
- Free Roam: blue

In the default palette, the tile position marker, position coordinate marker, segment, and name use the same color family and opacity within each effect type. Their roles are distinguished by marker shape.

## Options

The following settings are available in the Unity Mod Manager options window.

- Show or hide the camera frame
- Show or hide all effect markers
- Camera frame color
- Tile position marker color for each effect type
- Position coordinate marker color for each effect type
- Segment color for each effect type
- Name color for each effect type

</details>
