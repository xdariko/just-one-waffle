using DG.Tweening;
using UnityEngine;

// Общее для переноса предметов по столу (миска, вафля с вафельницы, вафля с доски украшения).
// Это просто функции, а не слой ввода: палец по-прежнему читает каждый скрипт сам через старый Input,
// как во всех мини-играх кухни (слой указателя давал отставание на кадр и ломал жесты).
public static class CarryHelper
{
    // Точка под пальцем/мышью на плоскости стола (z = 0)
    public static Vector3 ScreenToWorld(Camera cam)
    {
        var p = Input.mousePosition;
        p.z = -cam.transform.position.z;
        var w = cam.ScreenToWorldPoint(p);
        w.z = 0f;
        return w;
    }

    // Палец у края экрана — стол прокручивается в ту сторону (так предмет доезжает до соседней станции).
    // zone — доля ширины экрана у края, speed — мировых ед./сек
    public static void EdgeScroll(TableScroller scroller, float zone, float speed)
    {
        if (scroller == null) return;
        float x01 = Input.mousePosition.x / Mathf.Max(1f, Screen.width);
        if (x01 > 1f - zone) scroller.ScrollBy(speed * Time.deltaTime);
        else if (x01 < zone) scroller.ScrollBy(-speed * Time.deltaTime);
    }

    // Плавно вернуть предмет на место (позиция и размер). Анимация привязана к объекту: уничтожили — остановилась
    public static Tween ReturnTo(Transform target, Vector3 position, Vector3 scale, float time)
    {
        DOTween.Kill(target);
        return DOTween.Sequence().SetTarget(target).SetLink(target.gameObject)
            .Join(target.DOMove(position, time).SetEase(Ease.InOutSine))
            .Join(target.DOScale(scale, time).SetEase(Ease.InOutSine));
    }
}
