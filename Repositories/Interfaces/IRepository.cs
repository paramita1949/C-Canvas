using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ImageColorChanger.Repositories.Interfaces
{
    /// <summary>
    /// 仓储基础接口
    /// 定义所有仓储的通用CRUD操作
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public interface IRepository<TEntity> where TEntity : class
    {
        /// <summary>
        /// 根据ID获取实体
        /// </summary>
        Task<TEntity> GetByIdAsync(int id);

        /// <summary>
        /// 获取所有实体
        /// </summary>
        Task<IEnumerable<TEntity>> GetAllAsync();

        /// <summary>
        /// 根据条件查询实体
        /// </summary>
        Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// 添加实体
        /// </summary>
        Task<TEntity> AddAsync(TEntity entity);

        /// <summary>
        /// 批量添加实体
        /// </summary>
        Task AddRangeAsync(IEnumerable<TEntity> entities);

        /// <summary>
        /// 更新实体
        /// </summary>
        Task UpdateAsync(TEntity entity);

        /// <summary>
        /// 删除实体
        /// </summary>
        Task DeleteAsync(TEntity entity);

        /// <summary>
        /// 根据ID删除实体
        /// </summary>
        Task DeleteByIdAsync(int id);

        /// <summary>
        /// 批量删除实体
        /// </summary>
        Task DeleteRangeAsync(IEnumerable<TEntity> entities);

        /// <summary>
        /// 检查实体是否存在
        /// </summary>
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// 获取实体数量
        /// </summary>
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null);

        /// <summary>
        /// 保存更改
        /// </summary>
        Task<int> SaveChangesAsync();
    }
}

