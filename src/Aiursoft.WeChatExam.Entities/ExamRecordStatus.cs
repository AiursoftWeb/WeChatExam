namespace Aiursoft.WeChatExam.Entities;

public enum ExamRecordStatus
{
    /// <summary>
    /// 正在进行中
    /// </summary>
    InProgress = 0,

    /// <summary>
    /// 已提交（等待判分或已判分）
    /// </summary>
    Submitted = 1,
    
    /// <summary>
    /// 考试时间耗尽自动提交
    /// </summary>
    TimeOut = 2
}
