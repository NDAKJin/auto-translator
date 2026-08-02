## 设置页面UI格式  


### 基本设置  
___
当前语言:ChineseSimplified(简体中文)&emsp;&emsp;&emsp;语言:English   
开启动态翻译            <checkbox>√</checkbox>   
启动翻译记录            <checkbox>√</checkbox>  
启用实时翻译            <checkbox>√</checkbox>
### 平台设置
___
#### 可选平台 
<select>
  <option value="platform1">火山翻译</option>
  <option value="platform1">百度翻译</option>
  <option value="platform1">阿里翻译</option>
</select><br>      
访问标识: <input type="text"><br>  
访问密钥: <input type="text"><br>   
<button>设置</button>   

| 平台      | 状态 | 操作 |
| ----------- | ----------- | ----------- |
| 火山翻译      | 运行中       | <button>启用</button> |
| 百度翻译      | 临时限制       | <button>重启</button> |
| 阿里翻译      | 不可用       | <button>启用</button> |
###  翻译文本  
存储路径: xxxxx  <button>打开路径</button>

| 翻译键 | 类型 | 状态 | 原文 | 译文 |  操作 | 
| --- | --- | --- |--- | --- | --- | 
| key1 | xml | 已翻译 | you are a good man | 你是一个好人 | <button>翻译</button> |  
| ui.key1 | label | 未翻译 | you are a good man | you are a good man | <button>翻译</button> |  
| ui.key2 | tab | 未翻译 | you are a good man | you are a good man | <button>翻译</button> |  
<button>一键翻译</button>