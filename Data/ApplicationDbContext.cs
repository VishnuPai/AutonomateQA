using Microsoft.EntityFrameworkCore;
using UiTestRunner.Models;

namespace UiTestRunner.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<TestResult> TestResults { get; set; }
        public DbSet<TestCase> TestCases { get; set; }
    }
}
