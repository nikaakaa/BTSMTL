using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Editor
{
    public class RectangleSelector : MouseManipulator
    {
        class RectangleSelect : ImmediateModeElement
        {
            static Material lineMaterial;
            
            public Vector2 start { get; set; }

            public Vector2 end { get; set; }

            public Func<Vector2> offset { get; set; }

            public RectangleSelect()
            {
                if(lineMaterial == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    lineMaterial = new Material(shader);
                    lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    // Turn on alpha blending
                    lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    // Turn backface culling off
                    lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    // Turn off depth writes
                    lineMaterial.SetInt("_ZWrite", 0);
                }
            }

            protected override void ImmediateRepaint()
            {
                VisualElement visualElement = base.parent;
                Vector2 vector = start;
                Vector2 vector2 = end;
                if (!(start == end))
                {
                    Vector2 offsets = offset();

                    vector += visualElement.layout.position + offsets;
                    vector2 += visualElement.layout.position + offsets;
                    Rect rect = default(Rect);
                    rect.min = new Vector2(Math.Min(vector.x, vector2.x), Math.Min(vector.y, vector2.y));
                    rect.max = new Vector2(Math.Max(vector.x, vector2.x), Math.Max(vector.y, vector2.y));
                    Rect rect2 = rect;
                    Color col = new Color(1f, 0.6f, 0f, 1f);
                    float segmentsLength = 5f;
                    Vector3[] array = new Vector3[4]
                    {
                        new Vector3(rect2.xMin, rect2.yMin, 0f),
                        new Vector3(rect2.xMax, rect2.yMin, 0f),
                        new Vector3(rect2.xMax, rect2.yMax, 0f),
                        new Vector3(rect2.xMin, rect2.yMax, 0f)
                    };

                    GL.PushMatrix();
                    lineMaterial.SetPass(0);
                    DrawDottedLine(array[0], array[1], segmentsLength, col);
                    DrawDottedLine(array[1], array[2], segmentsLength, col);
                    DrawDottedLine(array[2], array[3], segmentsLength, col);
                    DrawDottedLine(array[3], array[0], segmentsLength, col);
                    GL.PopMatrix();
                }
            }

            void DrawDottedLine(Vector3 p1, Vector3 p2, float segmentsLength, Color col)
            {
                GL.Begin(1);
                GL.Color(col);
                float num = Vector3.Distance(p1, p2);
                int num2 = Mathf.CeilToInt(num / segmentsLength);
                for (int i = 0; i < num2; i += 2)
                {
                    GL.Vertex(Vector3.Lerp(p1, p2, (float)i * segmentsLength / num));
                    GL.Vertex(Vector3.Lerp(p1, p2, (float)(i + 1) * segmentsLength / num));
                }
                GL.End();
            }
        }

        readonly RectangleSelect m_Rectangle;

        bool m_Active;

        //
        // ժҪ:
        //     RectangleSelector's constructor.
        public RectangleSelector(Func<Vector2> offset = null)
        {
            base.activators.Add(new ManipulatorActivationFilter
            {
                button = MouseButton.LeftMouse
            });
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
            {
                base.activators.Add(new ManipulatorActivationFilter
                {
                    button = MouseButton.LeftMouse,
                    modifiers = EventModifiers.Command
                });
            }
            else
            {
                base.activators.Add(new ManipulatorActivationFilter
                {
                    button = MouseButton.LeftMouse,
                    modifiers = EventModifiers.Control
                });
            }



            m_Rectangle = new RectangleSelect();
            m_Rectangle.style.position = Position.Absolute;
            m_Rectangle.style.top = 0f;
            m_Rectangle.style.left = 0f;
            m_Rectangle.style.bottom = 0f;
            m_Rectangle.style.right = 0f;
            m_Rectangle.offset = offset;
            m_Active = false;
        }

        //
        // ժҪ:
        //     Computer the axis-aligned bound rectangle.
        //
        // ����:
        //   position:
        //     Rectangle to bound.
        //
        //   transform:
        //     Transform.
        //
        // ���ؽ��:
        //     The axis-aligned bound.
        public Rect ComputeAxisAlignedBound(Rect position, Matrix4x4 transform)
        {
            Vector3 vector = transform.MultiplyPoint3x4(position.min);
            Vector3 vector2 = transform.MultiplyPoint3x4(position.max);
            return Rect.MinMaxRect(Math.Min(vector.x, vector2.x), Math.Min(vector.y, vector2.y), Math.Max(vector.x, vector2.x), Math.Max(vector.y, vector2.y));
        }

        //
        // ժҪ:
        //     Called to register click event callbacks on the target element.
        protected override void RegisterCallbacksOnTarget()
        {
            base.target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            base.target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            base.target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            base.target.RegisterCallback<MouseCaptureOutEvent>(OnMouseCaptureOutEvent);
        }

        //
        // ժҪ:
        //     Called to unregister event callbacks from the target element.
        protected override void UnregisterCallbacksFromTarget()
        {
            base.target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            base.target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            base.target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            base.target.UnregisterCallback<MouseCaptureOutEvent>(OnMouseCaptureOutEvent);
        }

        private void OnMouseCaptureOutEvent(MouseCaptureOutEvent e)
        {
            if (m_Active)
            {
                m_Rectangle.RemoveFromHierarchy();
                m_Active = false;
            }
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            if (m_Active)
            {
                e.StopImmediatePropagation();
                return;
            }

            ISelection selection = base.target as ISelection;
            if (selection != null && base.target.panel?.GetCapturingElement(PointerId.mousePointerId) == null && CanStartManipulation(e))
            {
                if (!e.actionKey)
                {
                    selection.ClearSelection();
                }

                base.target.Add(m_Rectangle);
                m_Rectangle.start = e.localMousePosition;
                m_Rectangle.end = m_Rectangle.start;
                m_Active = true;
                base.target.CaptureMouse();
                e.StopImmediatePropagation();
            }
        }
        private void OnMouseUp(MouseUpEvent e)
        {
            if (!m_Active)
            {
                return;
            }

            ISelection selection = base.target as ISelection;
            if (selection == null || !CanStopManipulation(e))
            {
                return;
            }

            base.target.Remove(m_Rectangle);
            m_Rectangle.end = e.localMousePosition;
            Rect selectionRect = new Rect
            {
                min = new Vector2(Math.Min(m_Rectangle.start.x, m_Rectangle.end.x), Math.Min(m_Rectangle.start.y, m_Rectangle.end.y)),
                max = new Vector2(Math.Max(m_Rectangle.start.x, m_Rectangle.end.x), Math.Max(m_Rectangle.start.y, m_Rectangle.end.y))
            };
            selectionRect = ComputeAxisAlignedBound(selectionRect, selection.ContentContainer.transform.matrix.inverse);
            
            List<ISelectable> newSelection = new List<ISelectable>();
            for (int elementIndex = 0; elementIndex < selection.Elements.Count; elementIndex++)
            {
                ISelectable child = selection.Elements[elementIndex];
                Rect rectangle = base.target.ChangeCoordinatesTo(child as VisualElement, selectionRect);
                if (child.IsSelectable() && child.Overlaps(rectangle))
                    newSelection.Add(child);
            }
            foreach (ISelectable item in newSelection)
            {
                bool selected = false;
                for (int selectionIndex = 0; selectionIndex < selection.Selections.Count; selectionIndex++)
                {
                    if (!ReferenceEquals(selection.Selections[selectionIndex], item))
                        continue;
                    selected = true;
                    break;
                }
                if (selected)
                {
                    if (e.actionKey)
                    {
                        selection.RemoveFromSelection(item);
                    }
                }
                else
                {
                    selection.AddToSelection(item);
                }
            }

            m_Active = false;
            base.target.ReleaseMouse();
            e.StopPropagation();
        }
        private void OnMouseMove(MouseMoveEvent e)
        {
            if (m_Active)
            {
                m_Rectangle.end = e.localMousePosition;
                e.StopPropagation();
            }
        }
    }
}
