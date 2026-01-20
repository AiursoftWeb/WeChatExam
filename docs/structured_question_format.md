## 一、总体输出要求

* **输出格式**

  * 必须为 **原始 JSON**
  * **不得**使用 Markdown 包裹
  * **不得**输出除 JSON 之外的任何说明性文字

* **顶层结构**

  * 一个 **JSON 数组（Array）**
  * 数组中每个元素对应一个知识点（Knowledge Point）

---

## 二、顶层元素结构（Knowledge Object）

每个数组元素必须包含以下字段：

| 字段名              | 类型     | 是否必填 | 说明              |
| ---------------- | ------ | ---- | --------------- |
| KnowledgeTitle   | string | 是    | 知识点标题，简明概括该知识主题 |
| KnowledgeContent | string | 是    | 知识点的核心内容说明      |
| Questions        | array  | 是    | 该知识点下的题目列表      |

---

## 三、Questions 字段结构（Question Object）

`Questions` 是一个数组，每个元素代表一道题目，结构如下：

| 字段名             | 类型     | 是否必填 | 说明                              |
| --------------- | ------ | ---- | ------------------------------- |
| QuestionContent | string | 是    | 题干内容，可包含文本、图片链接、填空下划线（如 `____`） |
| QuestionType    | number | 是    | 题目类型（见第四部分）                     |
| Metadata        | object | 是    | 题目元数据，用于前端渲染                    |
| StandardAnswer  | string | 是    | 判卷标准或标准答案                       |
| Explanation     | string | 是    | 题目解析                            |
| Tags            | array  | 是    | 题目标签（字符串数组，用于分类或检索）             |

> **注意**：`Metadata` 类型由原来的 `array` 统一调整为 **object**

---

## 四、QuestionType 枚举说明（以当前前端题型为准）

目前支持的题型如下：

| 数值 | 类型名称        | 说明  |
| -- | ----------- | --- |
| 0  | Choice      | 单选题 |
| 1  | Blank       | 填空题 |
| 2  | Bool        | 判断题 |
| 3  | ShortAnswer | 简答题 |
| 4  | Essay       | 论述题 |

> 说明
>
> * 名词解释使用 **ShortAnswer（3）** 表示
> * 连线题目前不支持，不需要生成

---

## 五、Metadata 字段最新规范（重点更新）

### 1. 单选题（QuestionType = 0）

* **Metadata 为对象**
* **必须包含 options 字段**
* **options 为字符串数组**

示例：

```json
{
  "options": ["选项A", "选项B", "选项C", "选项D"]
}
```

---

### 2. 判断题（QuestionType = 2）

* **Metadata 为对象**
* **必须包含 options 字段**
* options 一般为两项（如“正确 / 错误”或其他业务定义）

示例：

```json
{
  "options": ["正确", "错误"]
}
```

---

### 3. 其他题型（填空题 / 名词解释 / 简答题 / 论述题）

* **Metadata 统一为空对象**
* 不得为 `null`、数组或省略

示例：

```json
{ }
```

---

## 六、StandardAnswer 字段规范（按题型）

### 1. 单选题（Choice）

* **格式**：正确选项的完整字符串
* **示例**：

```text
选项A
```

---

### 2. 填空题（Blank）

* **格式**：多个答案用英文逗号分隔
* **示例**：

```text
答案1,答案2
```

---

### 3. 判断题（Bool）

* **格式**：布尔值字符串

```text
true
```

或

```text
false
```

---

### 4. 名词解释 / 简答题 / 论述题

* **格式**：评分 prompt 字符串
* **内容必须包含**

  * 明确的得分点
  * 示例答案（或参考答案）

示例：

```text
得分点：
①概念定义准确；
②关键特征说明完整；
③表述清晰。
示例答案：……
```

---

## 七、Explanation 字段要求

* 对题目的解题思路、判断依据或评分逻辑进行说明
* 表述清晰、简洁、准确
* 不得仅重复 StandardAnswer

---

## 八、Tags 字段要求

* **类型**：字符串数组
* **用途**：知识分类、检索、标签化
* **示例**：

```json
["基础概念", "XX学科", "重点"]
```

---

## 九、完整结构示意（更新后示例）

```json
[
  {
    "KnowledgeTitle": "浪漫主义音乐代表人物",
    "KnowledgeContent": "浪漫主义时期的音乐强调情感表达与个性自由。",
    "Questions": [
      {
        "QuestionContent": "以下哪位作曲家属于浪漫主义时期？",
        "QuestionType": 0,
        "Metadata": {
          "options": ["肖邦", "巴赫", "海顿", "莫扎特"]
        },
        "StandardAnswer": "肖邦",
        "Explanation": "肖邦是浪漫主义时期钢琴音乐的代表人物。",
        "Tags": ["音乐史", "浪漫主义"]
      },
      {
        "QuestionContent": "肖邦是浪漫主义作曲家。",
        "QuestionType": 2,
        "Metadata": {
          "options": ["正确", "错误"]
        },
        "StandardAnswer": "true",
        "Explanation": "肖邦创作风格和历史分期均属于浪漫主义时期。",
        "Tags": ["音乐史", "判断题"]
      }
    ]
  }
]
```
