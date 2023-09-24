using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;

/// <summary>
/// 虚拟摇杆(暂作用于控制层)
/// </summary>
public class EasyTouchMove : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public List<Vector2> caluatePostionQueue;
    /// <summary>
    /// 摇杆最大半径
    /// 以像素为单位
    /// </summary>
    public float JoyStickRadius = 50;

    /// <summary>
    /// 摇杆重置所诉
    /// </summary>
    public float JoyStickResetSpeed = 5.0f;

    /// <summary>
    /// 当前物体的Transform组件
    /// </summary>
    private RectTransform selfTransform;

    /// <summary>
    /// 是否触摸了虚拟摇杆
    /// </summary>
    private bool isTouched = false;

    /// <summary>
    /// 虚拟摇杆的默认位置
    /// </summary>
    private Vector2 originPosition;

    /// <summary>
    /// 虚拟摇杆的移动方向
    /// </summary>
    private Vector2 touchedAxis;
    public Vector2 TouchedAxis
    {
        get
        {
            return touchedAxis.normalized;
        }
    }

    /// <summary>
    /// 定义触摸开始事件委托 
    /// </summary>
    public delegate void JoyStickTouchBegin(Vector2 vec);

    /// <summary>
    /// 定义触摸过程事件委托 
    /// </summary>
    /// <param name="vec">虚拟摇杆的移动方向</param>
    public delegate void JoyStickTouchMove(Vector2 vec);

    /// <summary>
    /// 定义触摸结束事件委托
    /// </summary>
    public delegate void JoyStickTouchEnd();

    /// <summary>
    /// 注册触摸开始事件
    /// </summary>
    public event JoyStickTouchBegin OnJoyStickTouchBegin;

    /// <summary>
    /// 注册触摸过程事件
    /// </summary>
    public event JoyStickTouchMove OnJoyStickTouchMove;

    /// <summary>
    /// 注册触摸结束事件
    /// </summary>
    public event JoyStickTouchEnd OnJoyStickTouchEnd;


    void Start()
    {
        //初始化虚拟摇杆的默认方向
        selfTransform = this.GetComponent<RectTransform>();
        originPosition = selfTransform.anchoredPosition;
    }
    public bool IsTouched()
    {
        return isTouched;
    }
    //开始控制摇杆
    public void OnPointerDown(PointerEventData eventData)
    {
        //MainPanelContrller.Instance.SetAutoMove(false);
        isTouched = true;
        touchedAxis = GetJoyStickAxis(eventData);
        if (this.OnJoyStickTouchBegin != null)
            this.OnJoyStickTouchBegin(TouchedAxis);
    }

    //松开摇杆
    public void OnPointerUp(PointerEventData eventData)
    {
        isTouched = false;
        selfTransform.anchoredPosition = originPosition;
        touchedAxis = Vector2.zero;
        if (this.OnJoyStickTouchEnd != null)
            this.OnJoyStickTouchEnd();

    }
    //拖动摇杆
    public void OnDrag(PointerEventData eventData)
    {
        touchedAxis = GetJoyStickAxis(eventData);
        if (this.OnJoyStickTouchMove != null)
            this.OnJoyStickTouchMove(TouchedAxis);
    }


    void Update()
    {

        //松开虚拟摇杆后让虚拟摇杆回到默认位置
        if (selfTransform.anchoredPosition.magnitude > originPosition.magnitude)
            selfTransform.anchoredPosition -= TouchedAxis * Time.deltaTime * JoyStickResetSpeed;

    }

    /// <summary>
    /// 返回虚拟摇杆的偏移量
    /// </summary>
    /// <returns>The joy stick axis.</returns>
    /// <param name="eventData">Event data.</param>
    private Vector2 GetJoyStickAxis(PointerEventData eventData)
    {
        //获取手指位置的世界坐标
        Vector3 worldPosition;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(selfTransform,
                 eventData.position, eventData.pressEventCamera, out worldPosition))
            selfTransform.position = worldPosition;
        //获取摇杆的偏移量
        Vector2 touchAxis = selfTransform.anchoredPosition - originPosition;
        //摇杆偏移量限制
        if (touchAxis.magnitude >= JoyStickRadius)
        {
            touchAxis = touchAxis.normalized * JoyStickRadius;
            selfTransform.anchoredPosition = touchAxis;
        }
        return touchAxis;
    }

}
