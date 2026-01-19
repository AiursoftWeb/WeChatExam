using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.MySql;

public class MySqlContext(DbContextOptions<MySqlContext> options) : WeChatExamDbContext(options);
