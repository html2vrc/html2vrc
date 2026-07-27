# UDOM

> 웹 UI의 의미를 특정 브라우저나 Unity 구현에 종속되지 않는 형태로 표현하는 중간 명세.

UDOM은 HTML2VRC의 Skill, React SDK와 Unity Renderer가 공유하는 계약이다.

- HTML2VRC Skill은 웹사이트와 이미지를 해석해 UDOM을 생성한다.
- React SDK는 HTML2VRC 컴포넌트의 현재 화면을 UDOM으로 표현한다.
- Renderer는 UDOM을 Unity Canvas, TextMeshPro, Material과 VRChat 호환 요소로 만든다.

UDOM은 렌더링 방법을 지정하지 않는다. `gradient`가 Shader, Vertex Color 또는 Texture 중 무엇으로 표현되는지는 Renderer가 결정한다.

## 상태

현재 명세는 **0.1 초안**이다. 구현과 fixture를 만들면서 호환성을 깨는 변경이 발생할 수 있다.

초기 명세는 다음 표준의 구조를 참고한다.

- [WHATWG DOM Standard](https://dom.spec.whatwg.org/): 순서가 있는 노드 트리, 부모와 자식 관계
- [WHATWG HTML Standard](https://html.spec.whatwg.org/): 요소와 텍스트로 구성된 문서 모델
- [glTF 2.0 Specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html): 자산 버전, 참조 가능한 리소스, 확장과 애플리케이션별 데이터

## 설계 원칙

- 사람이 읽고 AI가 생성·수정하기 쉬운 JSON을 사용한다.
- 문서는 하나의 루트를 가진 순서 있는 트리다.
- 모든 노드는 재생성과 진단에 사용할 안정적인 문자열 ID를 가진다.
- 레이아웃, 시각 표현과 인터랙션의 의미만 정의한다.
- Yoga, UIEffect, TextMeshPro 같은 구현체 이름을 포함하지 않는다.
- 임의의 JavaScript, C# 또는 Udon 코드를 포함하지 않는다.
- 지원되지 않는 기능은 `extensions`로 추가하며 조용히 의미를 바꾸지 않는다.
- 동일한 UDOM, Renderer 버전과 프로파일은 동일한 결과를 만들어야 한다.

## 저장소 범위

이 저장소는 다음을 소유한다.

- UDOM 핵심 명세
- JSON Schema
- 유효하거나 유효하지 않은 예제
- 구현체가 공통으로 사용할 conformance fixture
- 공식 확장 명세

다음은 이 저장소의 범위가 아니다.

- 웹사이트를 UDOM으로 추론하는 AI 프롬프트
- HTML과 CSS를 완전히 호환하는 변환기
- Unity에서 사용할 Shader와 레이아웃 라이브러리 선택
- React 상태와 이벤트를 Udon으로 변환하는 방법

AI의 추론 정책은 `html2vrc/skill`, Unity 구현은 `html2vrc/renderer`, React 제작 환경은 `html2vrc/react`가 담당한다.

## 문서

- [UDOM 0.1 초기 명세](./SPECIFICATION.md)

향후 JSON Schema와 fixture가 추가되면 명세 문서와 함께 버전을 관리한다.

## 최소 예시

```json
{
  "asset": {
    "version": "0.1",
    "generator": "html2vrc/skill"
  },
  "viewport": {
    "width": 1200,
    "height": 720
  },
  "root": {
    "type": "element",
    "id": "settings",
    "name": "view",
    "style": {
      "layout": {
        "mode": "flex",
        "width": "100%",
        "height": "100%"
      },
      "paint": {
        "backgrounds": [
          {
            "type": "linear-gradient",
            "angle": 135,
            "stops": [
              { "position": 0, "color": "#171A2BFF" },
              { "position": 1, "color": "#35245DFF" }
            ]
          }
        ]
      }
    },
    "children": [
      {
        "type": "text",
        "id": "settings-title",
        "value": "Settings",
        "style": {
          "text": {
            "fontSize": 48,
            "color": "#FFFFFFFF"
          }
        }
      },
      {
        "type": "element",
        "id": "music-toggle",
        "name": "toggle",
        "properties": {
          "checked": true
        },
        "bind": {
          "checked": "settings.musicEnabled"
        },
        "on": {
          "change": "settings.setMusicEnabled"
        }
      }
    ]
  }
}
```

## 라이선스

라이선스는 저장소 공개 전에 확정한다.
