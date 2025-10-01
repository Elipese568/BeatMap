using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeatMap.Setting;
/// <summary>
/// 处理非基础类型的特殊值编辑逻辑
/// </summary>
public interface ICustomTypeHandler
{
    object HandleCustomType(object currentValue, Type targetType);
    string CustomTypeToString(object currentValue, Type targetType);
}
