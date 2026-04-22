/**
 * @ksa/rpc-client - TypeScript/Bun client SDK for Kitten Space Agency RPC
 * 
 * Provides a simple interface to communicate with the KSA game engine
 * via Unix domain sockets.
 */

export { KsaRpcClient } from "./client";
export type {
  RpcRequest,
  RpcResponse,
  CraftInfo,
  ClientOptions,
} from "./types";
