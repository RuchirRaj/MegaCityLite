using System.Collections.Generic;
using UnityEngine;
using UnityObject = UnityEngine.Object;

public class ResourcePreloader : MonoBehaviour
{
    [SerializeField]
    List<UnityObject> m_Resources = new List<UnityObject>();

    public void SetResources(IReadOnlyCollection<UnityObject> resources)
    {
        m_Resources.Clear();
        m_Resources.AddRange(resources);
    }
}
