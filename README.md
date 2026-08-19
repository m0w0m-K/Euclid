# Euclid

**Euclid** is a mod for **A Dance of Fire and Ice** that adds geometric construction, coordinate snapping, measurement, and editor visualization tools.  
**Euclid**는 **A Dance of Fire and Ice** 에디터에 기하 작도, 좌표 스냅, 측정, 시각화 도구를 추가하는 모드입니다.

## Features / 기능
For each shape, you can:

각 도형에서는 다음 작업을 할 수 있습니다.

- Pick positions from editor tiles / 에디터 타일에서 위치 선택
- Pick positions from existing construction points / 이미 만든 작도 점에서 위치 선택
- Enter coordinates manually / 좌표 직접 입력
- Change its name / 이름 변경
- Change its color / 색상 변경
- Show or hide it / 표시·숨김
- Create intersection points from selected shapes / 선택한 도형들의 교점 생성

Supported intersection combinations include:
지원하는 교점 조합:

- Line × Line / 직선 × 직선
- Line × Circle / 직선 × 원
- Circle × Circle / 원 × 원

### Coordinate snapping / 좌표 스냅

Supported editor coordinates can be snapped to the construction geometry.

지원되는 에디터 좌표를 작도한 도형에 맞춰 스냅할 수 있습니다.

### Camera frame / 카메라 프레임

When a **Move Camera** event is selected, Euclid can display its camera frame in the editor.

**카메라 이동(Move Camera)** 이벤트를 선택하면 해당 이벤트의 카메라 프레임을 에디터 위에 표시할 수 있습니다.

The frame follows the selected event's position, zoom, and rotation.

프레임은 선택된 이벤트의 위치, 줌, 회전을 반영합니다.

### Effect position visualization / 이펙트 위치 시각화

Euclid can visualize positions and offsets for supported editor effects.

지원되는 에디터 효과의 위치와 이동 관계를 화면에 표시할 수 있습니다.

Currently supported / 현재 지원:

- Move Camera / 카메라 이동
- Move Track / 길 이동
- Position Track / 길 위치
- Free Roam / 자유 이동 구간

### Localization / 번역

Euclid follows the language selected in ADOFAI.  
Euclid는 얼불춤에서 선택한 언어를 자동으로 따릅니다.

Currently included / 현재 포함된 언어:

- English
- 한국어
- 简体中文
- 繁體中文
- 日本語
- Français
- Deutsch
- Русский
- Română
- Polski
- Español
- Português (Brasil)
- Tiếng Việt
- Čeština

Missing translations fall back to English.  
번역이 누락되면 영어로 대체됩니다.

## Compatibility / 호환성

Euclid currently targets **A Dance of Fire and Ice 3.3.0**.  
Euclid는 현재 **A Dance of Fire and Ice 3.3.0**을 대상으로 합니다.
