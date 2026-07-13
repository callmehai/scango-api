using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScanGo.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class DeleteFakeMaxUsers : Migration
    {
        // Xoá 10 user Max (pro_yearly) fake + đơn của họ (cascade) để hạ tổng doanh thu.
        // 3 user Max thật không đụng -> còn 3 Max. Down() không khôi phục (data đã xoá).
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM users WHERE id IN (
'8b4c2e97-405a-4917-9bfd-b1f6fa36ad85',
'fecbac49-5432-4143-b144-ff1d3b26fb8e',
'85321357-1497-4b3f-968a-50139e21beff',
'dd8b12f5-18fe-4ede-bb3a-7c46deef6824',
'bad02522-a54d-43d6-aba4-7cbe67489a95',
'3ca6514c-6c09-4b02-8268-22e8188b50cc',
'346da107-267f-4da3-bf0b-31f909366e9d',
'6faade6f-753d-4c59-8eaa-774f892f7d3f',
'a9e792d3-a7d9-4870-b287-e8340870a249',
'229b38e3-10bc-4313-b538-6ed3076e4181'
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // irreversible: các user Max fake đã bị xoá.
        }
    }
}
