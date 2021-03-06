﻿using System;
using System.Collections.Generic;
using NewLife.Collections;
using NewLife.Data;
using NewLife.Messaging;

namespace NewLife.Remoting
{
    /// <summary>Api主机</summary>
    public abstract class ApiHost : DisposeBase, IApiHost, IServiceProvider
    {
        #region 属性
        /// <summary>编码器</summary>
        public IEncoder Encoder { get; set; }

        /// <summary>处理器</summary>
        public IApiHandler Handler { get; set; }

        /// <summary>过滤器</summary>
        public IList<IFilter> Filters { get; } = new List<IFilter>();

        /// <summary>用户会话数据</summary>
        public IDictionary<String, Object> Items { get; set; } = new NullableDictionary<String, Object>();

        /// <summary>获取/设置 用户会话数据</summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public virtual Object this[String key] { get { return Items[key]; } set { Items[key] = value; } }

        /// <summary>是否在会话上复用控制器。复用控制器可确保同一个会话多次请求路由到同一个控制器对象实例</summary>
        public Boolean IsReusable { get; set; }
        #endregion

        #region 控制器管理
        /// <summary>接口动作管理器</summary>
        public IApiManager Manager { get; } = new ApiManager();

        /// <summary>注册服务提供类。该类的所有公开方法将直接暴露</summary>
        /// <typeparam name="TService"></typeparam>
        public void Register<TService>() where TService : class, new()
        {
            Manager.Register<TService>();
        }

        /// <summary>注册服务</summary>
        /// <param name="controller">控制器对象或类型</param>
        /// <param name="method">动作名称。为空时遍历控制器所有公有成员方法</param>
        public void Register(Object controller, String method)
        {
            Manager.Register(controller, method);
        }
        #endregion

        #region 请求处理
        /// <summary>收到请求</summary>
        public event EventHandler<ApiMessageEventArgs> Received;

        /// <summary>处理消息</summary>
        /// <param name="session"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        IMessage IApiHost.Process(IApiSession session, IMessage msg)
        {
            if (msg.Reply) return null;

            // 过滤器
            this.ExecuteFilter(session, msg, false);

            var rs = OnReceive(session, msg);

            // 过滤器
            this.ExecuteFilter(session, rs, true);

            return rs;
        }

        /// <summary>处理请求消息。重载、事件、控制器 共三种消息处理方式</summary>
        /// <param name="session"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        protected virtual IMessage OnReceive(IApiSession session, IMessage msg)
        {
            // 优先调用外部事件
            if (Received != null)
            {
                var e = new ApiMessageEventArgs
                {
                    Session = session,
                    Message = msg
                };
                Received(this, e);

                if (e.Handled) return e.Message;
            }

            var pk = msg.Payload;
            // 如果外部事件未处理，再交给处理器
            pk = ProcessHandler(session, pk);

            // 封装响应消息
            var rs = msg.CreateReply();
            rs.Payload = pk;

            return rs;
        }

        private Packet ProcessHandler(IApiSession session, Packet pk)
        {
            var enc = Encoder;

            // 这里会导致二次解码，因为解码以后才知道是不是请求
            var dic = enc.Decode(pk);

            var action = "";
            Object args = null;
            if (!enc.TryGet(dic, out action, out args)) return null;

            Object result = null;
            var code = 0;
            try
            {
                result = Handler.Execute(session, action, args as IDictionary<String, Object>).Result;
            }
            catch (Exception ex)
            {
                var aex = ex as ApiException;
                code = aex != null ? aex.Code : 500;
                result = ex;
            }

            // 编码响应数据包
            return enc.Encode(code, result);
        }
        #endregion

        #region 服务提供者
        /// <summary>服务提供者</summary>
        public IServiceProvider Provider { get; set; }

        /// <summary>获取服务提供者</summary>
        /// <param name="serviceType"></param>
        /// <returns></returns>
        public virtual Object GetService(Type serviceType)
        {
            if (serviceType == GetType()) return this;
            if (serviceType == typeof(ApiHost)) return this;
            if (serviceType == typeof(IApiHost)) return this;
            if (serviceType == typeof(IApiManager)) return Manager;
            if (serviceType == typeof(IEncoder) && Encoder != null) return Encoder;
            if (serviceType == typeof(IApiHandler) && Handler != null) return Handler;

            return Provider?.GetService(serviceType);
        }
        #endregion
    }
}