
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ADBRuntime
{
    using Mono;
    public enum ColliderCollisionTypeZh
    {
        총_충돌_I_멤버_반복 = 1,
        멤버_충돌만_I_멤버_반복 = 2,
        노드_충돌만_I_노드_반복 = 3,
        충돌을_계산하지_않음_I_노드_반복 = 4
    }

    public enum UpdateModeZh
    {
        Update= 1,
        FixedUpdate = 2,
        LateUpdate = 3,
    }
    [CustomEditor(typeof(ADBRuntimeController))]
    public class ADBRuntimeEditor : Editor
    //OYM：它的编辑器，我觉得我有必要把一部分方法写到里面去
    {

        ADBRuntimeController controller;
        private bool isDeleteCollider;
        private bool isGenerateColliderOpenTrigger;
        private const int max=64;
        public void OnEnable()
        {
            controller = target as ADBRuntimeController;

        }
        public override void OnInspectorGUI()
        {
            Color color;
            if (Application.isPlaying)
            {
                color = new Color(0.5F, 1, 1);
            }
            else
            {
                color = new Color(0.7f, 1.0f, 0.7f);
            }
            serializedObject.Update();
            //OYM：更新表现形式;
            if (!Application.isPlaying)
            {
                Titlebar("ADB 컨트롤러", color);
                //报错
                if (controller.settings == null)
                {
                    Titlebar("에러:전역 연결 설정은 비워둘 수 없습니다.!", new Color(0.7f, 0.3f, 0.3f));
                }               
                if (controller.generateKeyWordWhiteList==null|| controller.generateKeyWordWhiteList.Count==0)
                {
                    Titlebar("에러:누락된 키워드 식별", Color.yellow);
                }
                else if(controller.settings!=null)
                {
                    for (int i = 0; i < controller.generateKeyWordWhiteList.Count; i++)
                    {
                        if (!controller.settings.isContain(controller.generateKeyWordWhiteList[i]))
                        {
                            Titlebar("에러:키워드: " + controller.generateKeyWordWhiteList[i]+ "가 전역 연결 설정에 없습니다.!", Color.yellow);
                        }

                    }
                }
                if (controller.colliderControll!=null&& (controller.colliderControll.isGenerateSuccessful == -1))
                {
                    Titlebar("충돌 본체가 성공적으로 생성되지 않은 것 같습니다. Animator 스크립트 아래에 스크립트를 마운트해 보십시오.", Color.grey);
                }

                EditorGUILayout.PropertyField(serializedObject.FindProperty("settings"), new GUIContent("전역 연결 설정"), true);

                GUILayout.Space(5);
                Titlebar("=============== 노드 설정", color);
                if (controller.generateTransform==null)
                {
                    controller.generateTransform = controller.transform;
                }
                controller.generateTransform = (Transform)EditorGUILayout.ObjectField(new GUIContent("검색 시작점"), controller.generateTransform, typeof(Transform), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("generateKeyWordWhiteList"), new GUIContent("키워드 식별"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("blackListOfGenerateTransform"), new GUIContent("노드 블랙리스트"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("generateKeyWordBlackList"), new GUIContent("키워드 블랙리스트"), true);

                if (GUILayout.Button("노드 데이터 생성", GUILayout.Height(22.0f)))
                {
                    controller.ListCheck();
                    controller.InitializePoint();
                    controller.isDebug = true;
                }

                if (controller.allPointTrans != null)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("inspectorPointList"), new GUIContent("모든 노드 좌표 :" + controller.allPointTrans.Count), true);
                    GUILayout.Space(5);
                }
            }
            else
            {
                Titlebar("달리기", new Color(0.5F, 1, 1));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("settings"), new GUIContent("전역 연결 설정"), true);
                GUILayout.Space(10);

                Titlebar("=============== 노드 설정", color);

                if (GUILayout.Button("모든 노드 위치 재설정", GUILayout.Height(22.0f)))
                {
                    controller.RestoreRuntimePoint();
                }
                if (GUILayout.Button("모든 노드 데이터를 재설정하고 다시 실행", GUILayout.Height(22.0f)))
                {
                    controller.Reset();
                }
                if (controller.allPointTrans != null)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("inspectorPointList"), new GUIContent("모든 노드 좌표 :" + controller.allPointTrans?.Count), true);
                }
            }
            Titlebar("=============== 충돌기 설정", color);
            if (controller.overlapsColliderList != null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("overlapsColliderList"), new GUIContent("충돌기 목록 :" + controller.overlapsColliderList.Count), true);

            }
            GUILayout.Space(5);

            string key = controller.isGenerateColliderAutomaitc ? "생성" : "새로 고치다";

            if (GUILayout.Button(key + "충돌 바디", GUILayout.Height(22.0f)))
            {
                controller.initializeCollider();
                controller.UpdateOverlapsCollider();
            }
            if (controller.generateColliderList == null|| controller.generateColliderList.Count==0)
            {
                controller.isGenerateColliderAutomaitc = EditorGUILayout.Toggle("자동으로 바디 충돌 생성 ", controller.isGenerateColliderAutomaitc);
                if (controller.isGenerateColliderAutomaitc)
                {
                    controller. isGenerateColliderOpenTrigger = EditorGUILayout.Toggle("  ┗━생성된 충돌 바디는 트리거입니다. ", controller.isGenerateColliderOpenTrigger);
                }
                if (controller.isGenerateColliderAutomaitc)
                {
                    controller.isGenerateByAllPoint = EditorGUILayout.Toggle("  ┗━모든 노드를 참조로 사용 ", controller.isGenerateByAllPoint);
                }
                if (controller.isGenerateColliderAutomaitc)
                {
                    controller.isGenerateFinger = EditorGUILayout.Toggle("  ┗━손가락 생성 ", controller.isGenerateFinger);
                }
            }
            if (GUILayout.Button("생성된 모든 충돌 바디 삭제", GUILayout.Height(22.0f)))
            {
                if (EditorUtility.DisplayDialog("삭제해야 합니까??", "작업을 취소할 수 없습니다.", "ok", "cancel"))
                {
                    for (int i = 0; i < controller.overlapsColliderList?.Count; i++)
                    {
                        if (controller.overlapsColliderList[i] != null)
                        {
                            if (controller.overlapsColliderList[i].gameObject.GetComponents<Component>().Length <= 3)
                            {
                                DestroyImmediate(controller.overlapsColliderList[i].gameObject);
                            }
                            else
                            {
                                DestroyImmediate(controller.overlapsColliderList[i]);
                            }

                        }
                    }
                    controller.generateColliderList = null;

                    if (isDeleteCollider)
                    {
                        for (int i = 0; i < controller.overlapsColliderList?.Count; i++)
                        {
                            if (controller.overlapsColliderList[i] != null)
                            {
                                if (controller.overlapsColliderList[i].gameObject.GetComponents<Component>().Length <= 3)
                                {
                                    DestroyImmediate(controller.overlapsColliderList[i].gameObject);
                                }
                                else
                                {
                                    DestroyImmediate(controller.overlapsColliderList[i]);
                                }

                            }
                        }
                        controller.overlapsColliderList.Clear();
                    }
                }
            }
            isDeleteCollider = EditorGUILayout.Toggle("  ┗━자동으로 생성되지 않는 충돌 바디 포함 ", isDeleteCollider);


            GUILayout.Space(10);

            Titlebar("=============== 물리적 설정", color);
            controller.iteration = EditorGUILayout.IntSlider("반복 횟수", controller.iteration, 1, max * (controller.isParallel ? 8 : 8) * (controller.isDebug ? 2 : 1));
            controller.isRunAsync = EditorGUILayout.Toggle("여러 스레드에서 실행할지 여부", controller.isRunAsync);
            if (controller.isRunAsync)
            {
                controller.isParallel = EditorGUILayout.Toggle("  ┗━병렬 모드", controller.isParallel);
            }
            controller.updateMode = (UpdateMode)EditorGUILayout.EnumPopup("업데이트 모드", (UpdateModeZh)controller.updateMode);
            controller.colliderCollisionType = (ColliderCollisionType)EditorGUILayout.EnumPopup("충돌 모드", (ColliderCollisionTypeZh)controller.colliderCollisionType);


            GUILayout.Space(10);
            controller.bufferTime = EditorGUILayout.FloatField("평활화 시간 길이", controller.bufferTime);
            controller.isOptimize = EditorGUILayout.Toggle("이동 궤적 최적화(실험)", controller.isOptimize);

            GUILayout.Space(10);
            controller.windForceScale = EditorGUILayout.Slider("바람의 힘", controller.windForceScale, 0, 1);
            controller.isDebug = EditorGUILayout.Toggle("모든 보조선을 그릴지 여부", controller.isDebug);
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