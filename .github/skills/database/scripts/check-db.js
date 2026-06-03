import Database from 'better-sqlite3';

/**
 * 检查数据库结构和数据统计
 */
const db = new Database('./data/record.db');

// 获取所有表
const tables = db.prepare("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").all();
console.log('📊 数据库表清单:\n');

const tableSummary = [];

tables.forEach(tableObj => {
  const tableName = tableObj.name;

  // 跳过SQLite系统表
  if (tableName === 'sqlite_sequence') return;

  try {
    // 获取记录数
    const countResult = db.prepare(`SELECT COUNT(*) as cnt FROM ${tableName}`).get();
    const recordCount = countResult.cnt;

    // 获取表结构
    const cols = db.pragma(`table_info(${tableName})`);

    tableSummary.push({
      name: tableName,
      columns: cols.length,
      rows: recordCount,
      fields: cols.map(c => `${c.name}(${c.type})`).join(', ')
    });

    console.log(`✓ ${tableName.padEnd(25)} | 记录: ${String(recordCount).padStart(4)} | 字段: ${cols.length}`);
  } catch (error) {
    console.log(`✗ ${tableName.padEnd(25)} | 错误: ${error.message}`);
  }
});

console.log('\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━');
console.log(`总表数: ${tableSummary.length} | 总记录数: ${tableSummary.reduce((sum, t) => sum + t.rows, 0)}`);
console.log('━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n');

// 显示填充表的详细信息
console.log('📋 已填充表详情:\n');
const filledTables = tableSummary.filter(t => t.rows > 0);
filledTables.forEach(t => {
  console.log(`[${t.name}] - ${t.rows} 条记录`);
  const fields = t.fields.split(', ');
  fields.slice(0, 5).forEach(f => console.log(`  • ${f}`));
  if (fields.length > 5) console.log(`  ... 及 ${fields.length - 5} 个字段`);
  console.log();
});

db.close();
