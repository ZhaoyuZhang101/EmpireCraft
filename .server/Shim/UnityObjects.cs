// Unity 的物体、组件、节点：服务器里没有场景，只留个壳子让模组的初始化代码走得通
// (new GameObject、AddComponent、transform.SetParent 之类不出空引用)。
//# hand T:UnityEngine.GameObject
//# hand T:UnityEngine.Component
//# hand T:UnityEngine.Transform
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class GameObject : Object
    {
        private readonly List<Component> _components = new();
        private Transform _transform;
        public bool activeSelf { get; set; } = true;
        public bool activeInHierarchy => activeSelf;

        public GameObject()
        {
        }

        public GameObject(string name)
        {
            this.name = name;
        }

        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            if (components == null) return;
            foreach (Type type in components)
                if (type != null && typeof(Component).IsAssignableFrom(type) && !type.IsAbstract) Attach((Component)Activator.CreateInstance(type));
        }

        public Transform transform
        {
            get => _transform ??= Attach(new Transform());
            set => _transform = value;
        }

        private T Attach<T>(T component) where T : Component
        {
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public T AddComponent<T>() where T : Component => Attach(Activator.CreateInstance<T>());

        public T GetComponent<T>()
        {
            foreach (Component component in _components)
                if (component is T found) return found;
            return default;
        }

        public T GetComponentInParent<T>() => GetComponent<T>();
        public T GetComponentInChildren<T>() => GetComponent<T>();
        public void SetActive(bool value) => activeSelf = value;
    }

    public class Component : Object
    {
        private GameObject _gameObject;

        public GameObject gameObject
        {
            get => _gameObject ??= new GameObject(GetType().Name);
            set => _gameObject = value;
        }

        public virtual Transform transform
        {
            get => gameObject.transform;
            set => gameObject.transform = value;
        }

        public T GetComponent<T>() => this is T self ? self : gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) => GetComponent<T>();
        public T GetComponentInParent<T>(bool includeInactive = false) => GetComponent<T>();
        public T[] GetComponents<T>() => Array.Empty<T>();
        public T[] GetComponentsInChildren<T>() => Array.Empty<T>();
    }

    public class Transform : Component
    {
        private readonly List<Transform> _children = new();
        private Transform _parent;
        public override Transform transform
        {
            get => this;
            set { }
        }

        public int childCount => _children.Count;
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; } = Vector3.one;
        public Vector3 lossyScale { get; set; } = Vector3.one;
        public Vector3 position { get; set; }

        public Transform parent
        {
            get => _parent;
            set => SetParent(value);
        }

        public void SetParent(Transform p)
        {
            _parent?._children.Remove(this);
            _parent = p;
            p?._children.Add(this);
        }

        public void SetParent(Transform parent, bool worldPositionStays) => SetParent(parent);

        public Transform Find(string n)
        {
            foreach (Transform child in _children)
                if (child.gameObject.name == n) return child;
            return null;
        }

        public Transform GetChild(int index) => index >= 0 && index < _children.Count ? _children[index] : null;
        public IEnumerator GetEnumerator() => _children.GetEnumerator();

        public void SetAsFirstSibling()
        {
        }

        public void SetAsLastSibling()
        {
        }
    }
}
