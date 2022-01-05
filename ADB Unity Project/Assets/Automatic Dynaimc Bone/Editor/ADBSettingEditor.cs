using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ADBRuntime
{
    [CustomEditor(typeof(ADBSetting))]
    public class ADBSettingEditor : Editor
    {
        ADBSetting controller;
        bool showConstraintGlobal=false;
        bool showConstrainForce=false;
        private enum ColliderChoiceZh
        {
            머리 = 1 << 0,
            상체 = 1 << 1,
            하체 = 1 << 2,
            허벅지 = 1 << 3,
            송아지 = 1 << 4,
            큰팔 = 1 << 5,
            팔뚝 = 1 << 6,
            로트 = 1 << 7,
            피트 = 1 << 8,
            기타 = 1 << 9,
        }
        public void OnEnable()
        {
            controller = target as ADBSetting;
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Titlebar("노드 설정", Color.green);
            controller.useGlobal = !EditorGUILayout.Toggle("고급 곡선 모드", !controller.useGlobal);
            if (!controller.useGlobal)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gravityScaleCurve"), new GUIContent("중력 계수 곡선"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("freezeCurve"), new GUIContent("세계 강성 계수 곡선"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rigidScaleCurve"), new GUIContent("로컬 강성 계수 곡선"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("massCurve"), new GUIContent("유휴 곡선"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("moveByFixedPointCurve"), new GUIContent("속도 보정 곡선"), true);
                // EditorGUILayout.PropertyField(serializedObject.FindProperty("moveByPrePointCurve"), new GUIContent("PrePoint 곡선으로 이동"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceCompensationCurve"), new GUIContent("거리 보정 곡선"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("frictionCurve"), new GUIContent("마찰 곡선"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("addForceScaleCurve"), new GUIContent("추가 힘 계수 곡선"), true);
                GUILayout.Space(10);
                showConstraintGlobal = EditorGUILayout.Foldout(showConstraintGlobal, "멤버 계수 곡선");
                if (showConstraintGlobal)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralShrinkVerticalScaleCurve"), new GUIContent("수직 인접 로드 수축력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralStretchVerticalScaleCurve"), new GUIContent("수직 인접 부재 인장력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralShrinkHorizontalScaleCurve"), new GUIContent("수평 인접 멤버 수축 힘 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralStretchHorizontalScaleCurve"), new GUIContent("수평 인접 멤버 스트레칭 힘 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("shearShrinkScaleCurve"), new GUIContent("순 분포 부재 수축력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("shearStretchScaleCurve"), new GUIContent("순 분포 부재 인장력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingShrinkVerticalScaleCurve"), new GUIContent("수직 위상 막대 수축 힘 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingStretchVerticalScaleCurve"), new GUIContent("수직 위상 부재 인장력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingShrinkHorizontalScaleCurve"), new GUIContent("수평 위상 막대 수축력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingStretchHorizontalScaleCurve"), new GUIContent("수평 위상 부재 인장력 계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("circumferenceShrinkScaleCurve"), new GUIContent("방사선 분포-멤버-수축력-계수 곡선"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("circumferenceStretchScaleCurve"), new GUIContent("radiation distribution-member-stretch force-coefficient curve"), true);
                }
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("gravityScaleGlobal"), new GUIContent("중력 척도 값"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("freezeGlobal"), new GUIContent("세계 강성 계수 값"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rigidScaleGlobal"), new GUIContent("로컬 강성 계수 값"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("massGlobal"), new GUIContent("유휴 값"), true);

                EditorGUILayout.PropertyField(serializedObject.FindProperty("moveByFixedPointGlobal"), new GUIContent("속도 보정 값"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceCompensationGlobal"), new GUIContent("거리 보정 값"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("frictionGlobal"), new GUIContent("마찰 값"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("addForceScaleGlobal"), new GUIContent("추가 힘 계수 값"), true);
                // EditorGUILayout.PropertyField(serializedObject.FindProperty("moveByPrePointGlobal"), new GUIContent("PrePoint 값으로 이동"), true);


                GUILayout.Space(10);
                showConstraintGlobal = EditorGUILayout.Foldout(showConstraintGlobal, "멤버 힘 계수 값");
                if (showConstraintGlobal)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralShrinkVerticalScaleGlobal"), new GUIContent("수직 인접 막대 수축 힘 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralStretchVerticalScaleGlobal"), new GUIContent("수직 인접 부재 인장력 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralShrinkHorizontalScaleGlobal"), new GUIContent("수평 인접 멤버 수축 힘 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralStretchHorizontalScaleGlobal"), new GUIContent("수평 인접 멤버 스트레칭 힘 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("shearShrinkScaleGlobal"), new GUIContent("메쉬 분포 막대 수축 힘 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("shearStretchScaleGlobal"), new GUIContent("메쉬 분포-멤버-스트레치 힘-계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingShrinkVerticalScaleGlobal"), new GUIContent("수직 위상 막대 수축 힘 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingStretchVerticalScaleGlobal"), new GUIContent("수직 위상 부재 인장력 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingShrinkHorizontalScaleGlobal"), new GUIContent("수평 위상 막대 수축 힘 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingStretchHorizontalScaleGlobal"), new GUIContent("수평 위상 부재 인장력 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("circumferenceShrinkScaleGlobal"), new GUIContent("방사선 분포 부재 수축력 계수 값"), true);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("circumferenceStretchScaleGlobal"), new GUIContent("radiation distribution-member-tensile force-coefficient value"), true);
                }
            }
            GUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pointRadiuCurve"), new GUIContent("노드 충돌 볼륨 반경 곡선"), true);

            Titlebar("바 설정", Color.green);
            showConstrainForce = EditorGUILayout.Foldout(showConstrainForce, "멤버 기본 힘");
            if (showConstrainForce)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralShrinkVertical"), new GUIContent("수직 인접 부재 기반 수축력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralStretchVertical"), new GUIContent("인접 부재 수직 기초 인장력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralShrinkHorizontal"), new GUIContent("인접 멤버 수평 기본 수축력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("structuralStretchHorizontal"), new GUIContent("adjacent-member-horizontal base 인장력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shearShrink"), new GUIContent("순 분포 구성원 기반 수축력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("shearStretch"), new GUIContent("순 분포 부재 기반 인장력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingShrinkVertical"), new GUIContent("수직 위상 막대 기반 수축력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingStretchVertical"), new GUIContent("수직 위상 부재 기본 인장력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingShrinkHorizontal"), new GUIContent("수평 위상 막대 기본 수축력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bendingStretchHorizontal"), new GUIContent("수평 위상 멤버 기본 스트레칭 힘"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("circumferenceShrink"), new GUIContent("분산 방사선-구성원-기본 수축력"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("circumferenceStretch"), new GUIContent("분산 방사선-구성원-기본 스트레칭 힘"), true);
                GUILayout.Label("최종 부재력 = 부재력 계수 값 * 부재 기본력");
            }

            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeStructuralVertical"), new GUIContent("수직 인접 막대 켜기"), true);
            if (controller.isComputeStructuralVertical)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isCollideStructuralVertical"), new GUIContent("┗━ 수직 인접 멤버 충돌 허용"), true);
            }
            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeStructuralHorizontal"), new GUIContent("가로 인접 막대 열기"), true);
            if (controller.isComputeStructuralHorizontal)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isCollideStructuralHorizontal"), new GUIContent("┗━ 수평으로 인접한 멤버가 충돌하도록 허용"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isLoopRootPoints"), new GUIContent("┗━ 종단 간 연결 허용"), true);
            }
            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeShear"), new GUIContent("메시 분포 막대 열기"), true);
            if (controller.isComputeShear)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isCollideShear"), new GUIContent("┗━ 메쉬 분포 막대가 충돌하도록 허용"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isLoopRootPoints"), new GUIContent("┗━ 종단 간 연결 허용"), true);
            }
            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeBendingVertical"), new GUIContent("세로 막대 열기"), true);
            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeBendingHorizontal"), new GUIContent("가로 막대 열기"), true);
            if (controller.isComputeBendingHorizontal)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isLoopRootPoints"), new GUIContent("┗━ 종단 간 연결 허용"), true);
            }
            GUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeCircumference"), new GUIContent("열린 방사선 분배 막대"), true);

            GUILayout.Space(10);

            Titlebar("기타 설정", Color.green);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isDebugDraw"), new GUIContent("드로잉 로드"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isComputeVirtual"), new GUIContent("가상 노드 생성"), true);

            if (controller.isComputeVirtual)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("isAllowComputeOtherConstraint"), new GUIContent("┗━ 가상 노드가 다른 막대를 생성하도록 허용"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("virtualPointAxisLength"), new GUIContent("┗━가상 막대 길이"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ForceLookDown"), new GUIContent("┗━강제 종료"), true);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("isAutoComputeWeight"), new GUIContent("노드 품질 자동 계산"), true);
            if (!controller.isAutoComputeWeight)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("weightCurve"), new GUIContent("┗━ 품질 곡선"), true);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gravity"), new GUIContent("중력"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isFixGravityAxis"), new GUIContent("캐릭터의 회전에 따라 중력 축이 회전합니다."), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isFixedPointFreezeRotation"), new GUIContent("고정 노드가 고정 및 회전되었는지 여부"), true);
            controller.colliderChoice = (ColliderChoice)EditorGUILayout.EnumFlagsField("다음 유형의 충돌 바디 정보 수신", (ColliderChoiceZh)controller.colliderChoice);
            serializedObject.ApplyModifiedProperties();
        }

        void Titlebar(string text, Color color)
        {
            GUILayout.Space(12);

            var backgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = color;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(text);
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = backgroundColor;

            GUILayout.Space(3);
        }
    }
}


