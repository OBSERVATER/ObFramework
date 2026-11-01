## Integrated Pooling
使用数组栈的高度自定义对象池，内置ListPool和DictionaryPool
### 使用
#### `ListPool<T>`,`DictionaryPool<TK,TV>`
- `ListPool<T>.Get()`: 获取对应`List<T>`
- `DictionaryPool<TK,TV>.Get()`: 获取对应`Dictionary<TK,TV>`
- `ListPool<T>.Return(List<T> list)`: 归还对应`List<T>`到池子，数据会自动清除
- `DictionaryPool<TK,TV>.Return(Dictionary<TK,TV> dict)`: 归还对应`Dictionary<TK,TV>`到池子，数据会自动清除

此外还有拓展方法
```csharp
public static void ReleaseToPool<T>(this List<T> list);
public static void ReleaseToPool<TK, TV>(this Dictionary<TK, TV> dict);
```
可直接对列表和字典对象操作归还。

#### 自定义对象池
使用`APoolPolicy<T>`抽象类定义对象池对象如何创建，以及创建、归还时的对象操作。
使用`AObjectPool<T>`抽象类定义对象池，并注入`APoolPolicy<T>`规则。
`AObjectPool<T>`的API：
- `Get()`:获取对象
- `Return(T o)`:归还对象
### 原理
使用ArrayPool获取数据，`_count`索引控制创建或归还的对象。当容量不足时自动从ArrayPool借出容量*2的数组，迁移数据后归还数组。当对象不够会自动创建对象。